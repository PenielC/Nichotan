using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Staff;

/// <param name="InvitationPending">Invited but hasn't set a password yet.</param>
public record StaffMember(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyList<string> Roles,
    bool MfaEnabled,
    bool IsDisabled,
    bool InvitationPending,
    DateTime CreatedAtUtc,
    DateTime? LastSignInAtUtc)
{
    public static StaffMember From(UserAccount u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.Roles, u.TwoFactorEnabled, u.IsDisabled, !u.HasPassword, u.CreatedAtUtc, u.LastSignInAtUtc);
}

public record ListStaffQuery : IQuery<IReadOnlyList<StaffMember>>;

public class ListStaffHandler(IIdentityService identity, ICurrentUser current) : IQueryHandler<ListStaffQuery, IReadOnlyList<StaffMember>>
{
    public async ValueTask<IReadOnlyList<StaffMember>> Handle(ListStaffQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        return (await identity.ListAsync(staffOnly: true, cancellationToken)).Select(StaffMember.From).OrderBy(s => s.FirstName).ThenBy(s => s.LastName).ToList();
    }
}

// ─────────────────────────── Invitations ───────────────────────────

public record InviteStaffCommand(string Email, string FirstName, string LastName, IReadOnlyList<string> Roles) : ICommand<StaffMember>;

public class InviteStaffValidator : AbstractValidator<InviteStaffCommand>
{
    public InviteStaffValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
        RuleForEach(x => x.Roles).Must(Domain.Identity.Roles.IsStaffRole).WithMessage("'{PropertyValue}' is not a staff role.");
    }
}

public class InviteStaffHandler(
    IIdentityService identity,
    IAppDbContext db,
    IAuditLog audit,
    ICurrentUser current,
    SessionIssuer sessions,
    IEmailSender email,
    IAppUrls urls,
    ILogger<InviteStaffHandler> logger) : ICommandHandler<InviteStaffCommand, StaffMember>
{
    public async ValueTask<StaffMember> Handle(InviteStaffCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var roles = command.Roles.Distinct().ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var userId = await identity.CreateAsync(command.Email, password: null, command.FirstName, command.LastName, roles, emailConfirmed: false, cancellationToken);
        var token = await sessions.IssueLinkTokenAsync(userId, AuthTokenPurpose.StaffInvitation, LinkLifetimes.StaffInvitation, cancellationToken);
        audit.Add(AuditAction.StaffInvited, entityType: "User", entityId: userId.ToString(), details: new { roles });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var user = (await identity.FindByIdAsync(userId, cancellationToken))!;
        var inviter = (await identity.FindByIdAsync(current.UserId!.Value, cancellationToken))?.FullName ?? "An administrator";
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.StaffInvitation(user.Email, user.FirstName, inviter, $"{urls.WebBaseUrl}/accept-invitation?token={token}"), logger, cancellationToken);
        return StaffMember.From(user);
    }
}

public record ResendInvitationCommand(Guid UserId) : ICommand;

public class ResendInvitationHandler(
    IIdentityService identity,
    IAppDbContext db,
    ICurrentUser current,
    SessionIssuer sessions,
    IEmailSender email,
    IAppUrls urls,
    ILogger<ResendInvitationHandler> logger) : ICommandHandler<ResendInvitationCommand>
{
    public async ValueTask<Unit> Handle(ResendInvitationCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var user = await identity.FindByIdAsync(command.UserId, cancellationToken);
        if (user is null || !user.IsStaff) throw new NotFoundException("Staff member not found.");
        if (user.HasPassword) throw new DomainException("This person has already accepted their invitation.");

        var token = await sessions.IssueLinkTokenAsync(user.Id, AuthTokenPurpose.StaffInvitation, LinkLifetimes.StaffInvitation, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var inviter = (await identity.FindByIdAsync(current.UserId!.Value, cancellationToken))?.FullName ?? "An administrator";
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.StaffInvitation(user.Email, user.FirstName, inviter, $"{urls.WebBaseUrl}/accept-invitation?token={token}"), logger, cancellationToken);
        return Unit.Value;
    }
}

/// <summary>The invited person sets their password from the emailed link (which also confirms their email).</summary>
public record AcceptInvitationCommand(string Token, string Password) : ICommand;

public class AcceptInvitationValidator : AbstractValidator<AcceptInvitationCommand>
{
    public AcceptInvitationValidator()
    {
        RuleFor(x => x.Token).NotEmpty();
        RuleFor(x => x.Password).Password();
    }
}

public class AcceptInvitationHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, SessionIssuer sessions) : ICommandHandler<AcceptInvitationCommand>
{
    public async ValueTask<Unit> Handle(AcceptInvitationCommand command, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var token = await sessions.RedeemLinkTokenAsync(command.Token, AuthTokenPurpose.StaffInvitation, cancellationToken);
        var user = await identity.FindByIdAsync(token.UserId, cancellationToken);
        if (user is null || user.IsDisabled) throw new DomainException("This invitation is no longer valid. Please ask an administrator for a new one.");

        await identity.SetPasswordAsync(user.Id, command.Password, cancellationToken);
        await identity.ConfirmEmailAsync(user.Id, cancellationToken);
        audit.Add(AuditAction.StaffInvitationAccepted, entityType: "User", entityId: user.Id.ToString(), actorUserId: user.Id, actorEmail: user.Email);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Unit.Value;
    }
}

// ─────────────────────────── Roles, disabling, lost authenticators ───────────────────────────

public record UpdateStaffRolesCommand(Guid UserId, IReadOnlyList<string> Roles) : ICommand<StaffMember>;

public class UpdateStaffRolesValidator : AbstractValidator<UpdateStaffRolesCommand>
{
    public UpdateStaffRolesValidator()
    {
        RuleFor(x => x.Roles).NotEmpty().WithMessage("Choose at least one role.");
        RuleForEach(x => x.Roles).Must(Domain.Identity.Roles.IsStaffRole).WithMessage("'{PropertyValue}' is not a staff role.");
    }
}

public class UpdateStaffRolesHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current) : ICommandHandler<UpdateStaffRolesCommand, StaffMember>
{
    public async ValueTask<StaffMember> Handle(UpdateStaffRolesCommand command, CancellationToken cancellationToken)
    {
        var adminId = current.RequireAny(Roles.Administrator);
        var user = await StaffRules.FindStaffAsync(identity, command.UserId, cancellationToken);
        var roles = command.Roles.Distinct().ToList();
        var losesAdmin = user.Roles.Contains(Roles.Administrator) && !roles.Contains(Roles.Administrator);
        if (losesAdmin && user.Id == adminId) throw new DomainException("You can't remove your own administrator role.");
        if (losesAdmin) await StaffRules.EnsureAnotherActiveAdminAsync(identity, user.Id, cancellationToken);

        var from = user.Roles.Order().ToList();
        await identity.SetRolesAsync(user.Id, roles, cancellationToken);
        audit.Add(AuditAction.StaffRolesChanged, entityType: "User", entityId: user.Id.ToString(), details: new { from, to = roles.Order().ToList() });
        await db.SaveChangesAsync(cancellationToken);
        return StaffMember.From((await identity.FindByIdAsync(user.Id, cancellationToken))!);
    }
}

public record SetStaffDisabledCommand(Guid UserId, bool Disabled) : ICommand<StaffMember>;

public class SetStaffDisabledHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current, SessionIssuer sessions)
    : ICommandHandler<SetStaffDisabledCommand, StaffMember>
{
    public async ValueTask<StaffMember> Handle(SetStaffDisabledCommand command, CancellationToken cancellationToken)
    {
        var adminId = current.RequireAny(Roles.Administrator);
        var user = await StaffRules.FindStaffAsync(identity, command.UserId, cancellationToken);
        if (user.IsDisabled == command.Disabled) return StaffMember.From(user);
        if (command.Disabled)
        {
            if (user.Id == adminId) throw new DomainException("You can't disable your own account.");
            if (user.Roles.Contains(Roles.Administrator)) await StaffRules.EnsureAnotherActiveAdminAsync(identity, user.Id, cancellationToken);
        }

        await identity.SetDisabledAsync(user.Id, command.Disabled, cancellationToken);
        // Disabling takes effect at once: every session ends, and the API rejects their access token on the next request.
        if (command.Disabled) await sessions.RevokeAllAsync(user.Id, cancellationToken);
        audit.Add(command.Disabled ? AuditAction.StaffDisabled : AuditAction.StaffEnabled, entityType: "User", entityId: user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return StaffMember.From((await identity.FindByIdAsync(user.Id, cancellationToken))!);
    }
}

/// <summary>For a lost phone: removes the authenticator so the person sets up a new one at their next sign-in.</summary>
public record ResetStaffMfaCommand(Guid UserId) : ICommand<StaffMember>;

public class ResetStaffMfaHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current, SessionIssuer sessions)
    : ICommandHandler<ResetStaffMfaCommand, StaffMember>
{
    public async ValueTask<StaffMember> Handle(ResetStaffMfaCommand command, CancellationToken cancellationToken)
    {
        var adminId = current.RequireAny(Roles.Administrator);
        var user = await StaffRules.FindStaffAsync(identity, command.UserId, cancellationToken);
        if (user.Id == adminId) throw new DomainException("Ask another administrator to reset your own authenticator.");
        await identity.SetTwoFactorEnabledAsync(user.Id, false, cancellationToken);
        await sessions.RevokeAllAsync(user.Id, cancellationToken);
        audit.Add(AuditAction.MfaDisabled, entityType: "User", entityId: user.Id.ToString(), details: new { resetByAdministrator = true });
        await db.SaveChangesAsync(cancellationToken);
        return StaffMember.From((await identity.FindByIdAsync(user.Id, cancellationToken))!);
    }
}

internal static class StaffRules
{
    public static async Task<UserAccount> FindStaffAsync(IIdentityService identity, Guid userId, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsStaff) throw new NotFoundException("Staff member not found.");
        return user;
    }

    /// <summary>Nichotan must never be left without an administrator who can sign in.</summary>
    public static async Task EnsureAnotherActiveAdminAsync(IIdentityService identity, Guid exceptUserId, CancellationToken cancellationToken)
    {
        var others = (await identity.ListAsync(staffOnly: true, cancellationToken))
            .Count(u => u.Id != exceptUserId && !u.IsDisabled && u.HasPassword && u.Roles.Contains(Roles.Administrator));
        if (others == 0) throw new DomainException("There must always be at least one active administrator.");
    }
}
