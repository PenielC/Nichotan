using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Identity;

// ─────────────────────────── Candidate self-registration ───────────────────────────

public record RegisterCandidateCommand(string FirstName, string LastName, string Email, string Password, string? Phone) : ICommand<AuthSession>;

public class RegisterCandidateValidator : AbstractValidator<RegisterCandidateCommand>
{
    public RegisterCandidateValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).Password();
        RuleFor(x => x.Phone).MaximumLength(30).MustBeInternationalNumber();
    }
}

public class RegisterCandidateHandler(
    IIdentityService identity,
    IAppDbContext db,
    IAuditLog audit,
    SessionIssuer sessions,
    IEmailSender email,
    IAppUrls urls,
    ILogger<RegisterCandidateHandler> logger) : ICommandHandler<RegisterCandidateCommand, AuthSession>
{
    public async ValueTask<AuthSession> Handle(RegisterCandidateCommand command, CancellationToken cancellationToken)
    {
        // Identity saves on its own, so a transaction keeps the account, profile and audit entry all-or-nothing.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var userId = await identity.CreateAsync(command.Email, command.Password, command.FirstName, command.LastName, [Roles.Candidate], emailConfirmed: false, cancellationToken);
        var user = (await identity.FindByIdAsync(userId, cancellationToken))!;

        db.CandidateProfiles.Add(new CandidateProfile(userId, command.FirstName, command.LastName, user.Email, PhoneNumbers.Normalise(command.Phone)));
        var verifyToken = await sessions.IssueLinkTokenAsync(userId, AuthTokenPurpose.EmailVerification, LinkLifetimes.EmailVerification, cancellationToken);
        audit.Add(AuditAction.CandidateRegistered, entityType: "User", entityId: userId.ToString(), actorUserId: userId, actorEmail: user.Email);
        var session = sessions.Issue(user, mfaSatisfied: false);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await SendSafely(email, EmailTemplates.VerifyEmail(user.Email, user.FirstName, $"{urls.WebBaseUrl}/verify-email?token={verifyToken}"), logger, cancellationToken);
        return session;
    }

    /// <summary>A mail outage must not undo a registration; the person can ask for the email again.</summary>
    internal static async Task SendSafely(IEmailSender sender, EmailMessage message, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            // The address isn't logged, only that a send failed.
            logger.LogError(ex, "Sending email \"{Subject}\" failed", message.Subject);
        }
    }
}

// ─────────────────────────── Sign in ───────────────────────────

public record LoginCommand(string Email, string Password) : ICommand<LoginResult>;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public class LoginHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions, ITokenService tokens)
    : ICommandHandler<LoginCommand, LoginResult>
{
    public async ValueTask<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await identity.FindByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            audit.Add(AuditAction.LoginFailed, AuditOutcome.Failure, details: new { reason = "unknown account" }, actorEmail: email);
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException();
        }

        var check = await identity.CheckPasswordAsync(user.Id, command.Password, cancellationToken);
        switch (check)
        {
            case PasswordCheck.LockedOut:
                audit.Add(AuditAction.LoginLockedOut, AuditOutcome.Denied, "User", user.Id.ToString(), actorUserId: user.Id, actorEmail: user.Email);
                await db.SaveChangesAsync(cancellationToken);
                throw new AccountLockedException();
            case PasswordCheck.Disabled:
            case PasswordCheck.Invalid:
                // A disabled account gets the same message as a wrong password, so it can't be probed.
                audit.Add(AuditAction.LoginFailed, AuditOutcome.Failure, "User", user.Id.ToString(),
                    new { reason = check == PasswordCheck.Disabled ? "account disabled" : "wrong password" }, user.Id, user.Email);
                await db.SaveChangesAsync(cancellationToken);
                throw new AuthenticationFailedException();
        }

        if (user.TwoFactorEnabled)
        {
            return new LoginResult(null, tokens.CreateMfaChallengeToken(user.Id));
        }

        audit.Add(AuditAction.LoginSucceeded, entityType: "User", entityId: user.Id.ToString(), actorUserId: user.Id, actorEmail: user.Email);
        await identity.RecordSignInAsync(user.Id, cancellationToken);
        var session = sessions.Issue(user, mfaSatisfied: false);
        await db.SaveChangesAsync(cancellationToken);
        return new LoginResult(session, null);
    }
}

/// <summary>Second step of a sign-in: a code from the authenticator app, or a one-time recovery code.</summary>
public record CompleteMfaLoginCommand(string ChallengeToken, string Code, bool IsRecoveryCode) : ICommand<AuthSession>;

public class CompleteMfaLoginValidator : AbstractValidator<CompleteMfaLoginCommand>
{
    public CompleteMfaLoginValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
    }
}

public class CompleteMfaLoginHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions, ITokenService tokens)
    : ICommandHandler<CompleteMfaLoginCommand, AuthSession>
{
    public async ValueTask<AuthSession> Handle(CompleteMfaLoginCommand command, CancellationToken cancellationToken)
    {
        var userId = tokens.ReadMfaChallengeToken(command.ChallengeToken) ?? throw new AuthenticationFailedException("Your sign-in has expired. Please sign in again.");
        var user = await identity.FindByIdAsync(userId, cancellationToken);
        if (user is null || user.IsDisabled) throw new AuthenticationFailedException("Your sign-in has expired. Please sign in again.");

        var code = command.Code.Replace(" ", string.Empty).Replace("-", string.Empty);
        var ok = command.IsRecoveryCode
            ? await identity.RedeemRecoveryCodeAsync(user.Id, command.Code.Trim(), cancellationToken)
            : await identity.VerifyAuthenticatorCodeAsync(user.Id, code, cancellationToken);
        if (!ok)
        {
            audit.Add(AuditAction.MfaChallengeFailed, AuditOutcome.Failure, "User", user.Id.ToString(),
                new { method = command.IsRecoveryCode ? "recovery code" : "authenticator" }, user.Id, user.Email);
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("That code didn't work. Check your authenticator app and try again.");
        }

        audit.Add(AuditAction.MfaChallengePassed, entityType: "User", entityId: user.Id.ToString(),
            details: new { method = command.IsRecoveryCode ? "recovery code" : "authenticator" }, actorUserId: user.Id, actorEmail: user.Email);
        audit.Add(AuditAction.LoginSucceeded, entityType: "User", entityId: user.Id.ToString(), actorUserId: user.Id, actorEmail: user.Email);
        await identity.RecordSignInAsync(user.Id, cancellationToken);
        var session = sessions.Issue(user, mfaSatisfied: true);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }
}

// ─────────────────────────── Refresh and sign out ───────────────────────────

public record RefreshSessionCommand(string RefreshToken) : ICommand<AuthSession>;

public class RefreshSessionHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions, IClock clock)
    : ICommandHandler<RefreshSessionCommand, AuthSession>
{
    public async ValueTask<AuthSession> Handle(RefreshSessionCommand command, CancellationToken cancellationToken)
    {
        var hash = AuthToken.Hash(command.RefreshToken ?? string.Empty);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken)
            ?? throw new AuthenticationFailedException("Your session has ended. Please sign in again.");
        var now = clock.UtcNow;

        if (token.RevokedAtUtc is not null && token.ReplacedById is not null)
        {
            // An already-rotated token came back: someone else has a copy. End every session in this family.
            await db.RefreshTokens.Where(t => t.FamilyId == token.FamilyId && t.RevokedAtUtc == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
            audit.Add(AuditAction.RefreshTokenReuseDetected, AuditOutcome.Denied, "User", token.UserId.ToString(), actorUserId: token.UserId);
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("Your session has ended. Please sign in again.");
        }

        if (!token.IsActive(now)) throw new AuthenticationFailedException("Your session has ended. Please sign in again.");

        var user = await identity.FindByIdAsync(token.UserId, cancellationToken);
        if (user is null || user.IsDisabled)
        {
            token.Revoke(now);
            await db.SaveChangesAsync(cancellationToken);
            throw new AuthenticationFailedException("Your session has ended. Please sign in again.");
        }

        var session = sessions.Issue(user, token.MfaSatisfied, token.FamilyId);
        token.Revoke(now, sessions.LastIssued!.Id);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }
}

public record LogoutCommand(string? RefreshToken) : ICommand;

public class LogoutHandler(IAppDbContext db, IAuditLog audit, IClock clock) : ICommandHandler<LogoutCommand>
{
    public async ValueTask<Unit> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.RefreshToken)) return Unit.Value;
        var hash = AuthToken.Hash(command.RefreshToken);
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is not null && token.RevokedAtUtc is null)
        {
            token.Revoke(clock.UtcNow);
            audit.Add(AuditAction.LoggedOut, entityType: "User", entityId: token.UserId.ToString(), actorUserId: token.UserId);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Unit.Value;
    }
}

// ─────────────────────────── Email verification ───────────────────────────

public record VerifyEmailCommand(string Token) : ICommand;

public class VerifyEmailHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions) : ICommandHandler<VerifyEmailCommand>
{
    public async ValueTask<Unit> Handle(VerifyEmailCommand command, CancellationToken cancellationToken)
    {
        var token = await sessions.RedeemLinkTokenAsync(command.Token, AuthTokenPurpose.EmailVerification, cancellationToken);
        await identity.ConfirmEmailAsync(token.UserId, cancellationToken);
        audit.Add(AuditAction.EmailVerified, entityType: "User", entityId: token.UserId.ToString(), actorUserId: token.UserId);
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public record ResendVerificationCommand : ICommand;

public class ResendVerificationHandler(
    IIdentityService identity,
    IAppDbContext db,
    ICurrentUser current,
    SessionIssuer sessions,
    IEmailSender email,
    IAppUrls urls,
    ILogger<ResendVerificationHandler> logger) : ICommandHandler<ResendVerificationCommand>
{
    public async ValueTask<Unit> Handle(ResendVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        if (user.EmailConfirmed) return Unit.Value;
        var token = await sessions.IssueLinkTokenAsync(user.Id, AuthTokenPurpose.EmailVerification, LinkLifetimes.EmailVerification, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.VerifyEmail(user.Email, user.FirstName, $"{urls.WebBaseUrl}/verify-email?token={token}"), logger, cancellationToken);
        return Unit.Value;
    }
}

// ─────────────────────────── Password reset ───────────────────────────

/// <summary>Always "succeeds", so the form can't be used to find out who has an account.</summary>
public record ForgotPasswordCommand(string Email) : ICommand;

public class ForgotPasswordValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public class ForgotPasswordHandler(
    IIdentityService identity,
    IAppDbContext db,
    IAuditLog audit,
    SessionIssuer sessions,
    IEmailSender email,
    IAppUrls urls,
    ILogger<ForgotPasswordHandler> logger) : ICommandHandler<ForgotPasswordCommand>
{
    public async ValueTask<Unit> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByEmailAsync(command.Email.Trim().ToLowerInvariant(), cancellationToken);
        // Invited staff who haven't set a password yet use their invitation instead.
        if (user is null || user.IsDisabled || !user.HasPassword) return Unit.Value;

        var token = await sessions.IssueLinkTokenAsync(user.Id, AuthTokenPurpose.PasswordReset, LinkLifetimes.PasswordReset, cancellationToken);
        audit.Add(AuditAction.PasswordResetRequested, entityType: "User", entityId: user.Id.ToString(), actorEmail: user.Email);
        await db.SaveChangesAsync(cancellationToken);
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.ResetPassword(user.Email, user.FirstName, $"{urls.WebBaseUrl}/reset-password?token={token}"), logger, cancellationToken);
        return Unit.Value;
    }
}

public record ResetPasswordCommand(string Token, string NewPassword) : ICommand;

public class ResetPasswordValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.NewPassword).Password();
    }
}

public class ResetPasswordHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions) : ICommandHandler<ResetPasswordCommand>
{
    public async ValueTask<Unit> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var token = await sessions.RedeemLinkTokenAsync(command.Token, AuthTokenPurpose.PasswordReset, cancellationToken);
        await identity.SetPasswordAsync(token.UserId, command.NewPassword, cancellationToken);
        // The link reached the inbox, which also proves ownership of the email.
        await identity.ConfirmEmailAsync(token.UserId, cancellationToken);
        await sessions.RevokeAllAsync(token.UserId, cancellationToken);
        audit.Add(AuditAction.PasswordReset, entityType: "User", entityId: token.UserId.ToString(), actorUserId: token.UserId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}

// ─────────────────────────── The signed-in user ───────────────────────────

public record MeResult(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles,
    bool IsStaff,
    bool EmailVerified,
    bool MfaEnabled,
    bool MfaSatisfied,
    bool MfaSetupRequired,
    Guid? CandidateProfileId);

public record GetMeQuery : IQuery<MeResult>;

public class GetMeHandler(IIdentityService identity, IAppDbContext db, ICurrentUser current, SessionIssuer sessions) : IQueryHandler<GetMeQuery, MeResult>
{
    public async ValueTask<MeResult> Handle(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        var profileId = await db.CandidateProfiles.Where(p => p.UserId == user.Id).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(cancellationToken);
        return new MeResult(user.Id, user.Email, user.FirstName, user.LastName, user.Roles, user.IsStaff, user.EmailConfirmed, user.TwoFactorEnabled,
            current.MfaSatisfied, sessions.MfaSetupRequired(user), profileId);
    }
}
