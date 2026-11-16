using FluentValidation;
using Mediator;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;

namespace Nichotan.Application.Identity;

/// <summary>What to show while setting up an authenticator app: the key and the QR code's otpauth URI.</summary>
public record MfaSetupResult(string SharedKey, string AuthenticatorUri);

public record GetMfaSetupQuery : IQuery<MfaSetupResult>;

public class GetMfaSetupHandler(IIdentityService identity, ICurrentUser current) : IQueryHandler<GetMfaSetupQuery, MfaSetupResult>
{
    public async ValueTask<MfaSetupResult> Handle(GetMfaSetupQuery query, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        if (user.TwoFactorEnabled) throw new DomainException("An authenticator app is already set up.");
        var (key, uri) = await identity.GetAuthenticatorSetupAsync(user.Id, cancellationToken);
        return new MfaSetupResult(key, uri);
    }
}

/// <summary>Recovery codes, shown once. Each works one time if the phone is lost.</summary>
public record RecoveryCodesResult(IReadOnlyList<string> RecoveryCodes);

public record EnableMfaCommand(string Code) : ICommand<RecoveryCodesResult>;

public class MfaCodeValidator : AbstractValidator<EnableMfaCommand>
{
    public MfaCodeValidator() => RuleFor(x => x.Code).NotEmpty().MaximumLength(10);
}

public class EnableMfaHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current) : ICommandHandler<EnableMfaCommand, RecoveryCodesResult>
{
    public async ValueTask<RecoveryCodesResult> Handle(EnableMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        if (user.TwoFactorEnabled) throw new DomainException("An authenticator app is already set up.");
        if (!await identity.VerifyAuthenticatorCodeAsync(user.Id, command.Code.Replace(" ", string.Empty), cancellationToken))
        {
            throw new DomainException("That code didn't match. Check the time on your phone and try the newest code.");
        }

        await identity.SetTwoFactorEnabledAsync(user.Id, true, cancellationToken);
        var codes = await identity.GenerateRecoveryCodesAsync(user.Id, cancellationToken);
        audit.Add(AuditAction.MfaEnabled, entityType: "User", entityId: user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return new RecoveryCodesResult(codes);
    }
}

public record DisableMfaCommand(string Code) : ICommand;

public class DisableMfaHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current, IOptions<SecurityOptions> security)
    : ICommandHandler<DisableMfaCommand>
{
    public async ValueTask<Unit> Handle(DisableMfaCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        if (!user.TwoFactorEnabled) return Unit.Value;
        if (user.IsStaff && security.Value.RequireMfaForStaff)
        {
            throw new DomainException("Staff accounts must keep an authenticator app. To move to a new phone, ask an administrator to reset it.");
        }

        if (!await identity.VerifyAuthenticatorCodeAsync(user.Id, command.Code.Replace(" ", string.Empty), cancellationToken))
        {
            throw new DomainException("That code didn't match.");
        }

        await identity.SetTwoFactorEnabledAsync(user.Id, false, cancellationToken);
        audit.Add(AuditAction.MfaDisabled, entityType: "User", entityId: user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

public record RegenerateRecoveryCodesCommand(string Code) : ICommand<RecoveryCodesResult>;

public class RegenerateRecoveryCodesHandler(IIdentityService identity, IAppDbContext db, IAuditLog audit, ICurrentUser current)
    : ICommandHandler<RegenerateRecoveryCodesCommand, RecoveryCodesResult>
{
    public async ValueTask<RecoveryCodesResult> Handle(RegenerateRecoveryCodesCommand command, CancellationToken cancellationToken)
    {
        var user = await identity.FindByIdAsync(current.UserId!.Value, cancellationToken) ?? throw new NotFoundException();
        if (!user.TwoFactorEnabled) throw new DomainException("Set up an authenticator app first.");
        if (!await identity.VerifyAuthenticatorCodeAsync(user.Id, command.Code.Replace(" ", string.Empty), cancellationToken))
        {
            throw new DomainException("That code didn't match.");
        }

        var codes = await identity.GenerateRecoveryCodesAsync(user.Id, cancellationToken);
        audit.Add(AuditAction.RecoveryCodesRegenerated, entityType: "User", entityId: user.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return new RecoveryCodesResult(codes);
    }
}
