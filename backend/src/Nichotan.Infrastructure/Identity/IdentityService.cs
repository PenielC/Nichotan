using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;
using Nichotan.Infrastructure.Persistence;

namespace Nichotan.Infrastructure.Identity;

public class IdentityService(UserManager<AppUser> users, AppDbContext db, IClock clock) : IIdentityService
{
    private const string AuthenticatorIssuer = "Nichotan";
    private const int RecoveryCodeCount = 10;

    public async Task<UserAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByEmailAsync(email.Trim());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<UserAccount?> FindByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        return user is null ? null : await ToAccountAsync(user);
    }

    public async Task<IReadOnlyList<UserAccount>> ListAsync(bool staffOnly, CancellationToken cancellationToken = default)
    {
        var rows = await (
            from u in db.Users.AsNoTracking()
            join ur in db.UserRoles on u.Id equals ur.UserId into urs
            from ur in urs.DefaultIfEmpty()
            join r in db.Roles on ur.RoleId equals r.Id into rs
            from r in rs.DefaultIfEmpty()
            select new { User = u, Role = r == null ? null : r.Name }).ToListAsync(cancellationToken);

        return rows.GroupBy(x => x.User.Id)
            .Select(g => ToAccount(g.First().User, g.Where(x => x.Role != null).Select(x => x.Role!).Order().ToList()))
            .Where(a => !staffOnly || a.IsStaff)
            .ToList();
    }

    public async Task<Guid> CreateAsync(string email, string? password, string firstName, string lastName, IReadOnlyCollection<string> roles, bool emailConfirmed, CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = normalized,
            Email = normalized,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            EmailConfirmed = emailConfirmed,
            CreatedAtUtc = clock.UtcNow,
        };

        var result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName)))
            {
                throw new ConflictException("An account with this email already exists. Try signing in or resetting your password.");
            }

            throw new DomainException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        Check(await users.AddToRolesAsync(user, roles));
        return user.Id;
    }

    public async Task<PasswordCheck> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        if (user.IsDisabled) return PasswordCheck.Disabled;
        if (await users.IsLockedOutAsync(user)) return PasswordCheck.LockedOut;
        if (await users.CheckPasswordAsync(user, password))
        {
            await users.ResetAccessFailedCountAsync(user);
            return PasswordCheck.Success;
        }

        await users.AccessFailedAsync(user);
        return await users.IsLockedOutAsync(user) ? PasswordCheck.LockedOut : PasswordCheck.Invalid;
    }

    public async Task SetPasswordAsync(Guid userId, string newPassword, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        // Check the policy first, so a weak password never leaves the account without one.
        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var result = await validator.ValidateAsync(users, user, newPassword);
            if (!result.Succeeded) errors.AddRange(result.Errors.Select(e => e.Description));
        }

        if (errors.Count > 0) throw new DomainException(string.Join(" ", errors.Distinct()));

        user.PasswordHash = users.PasswordHasher.HashPassword(user, newPassword);
        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        Check(await users.UpdateSecurityStampAsync(user));
    }

    public async Task ConfirmEmailAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        if (user.EmailConfirmed) return;
        user.EmailConfirmed = true;
        Check(await users.UpdateAsync(user));
    }

    public async Task SetRolesAsync(Guid userId, IReadOnlyCollection<string> roles, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        var current = await users.GetRolesAsync(user);
        Check(await users.RemoveFromRolesAsync(user, current.Except(roles)));
        Check(await users.AddToRolesAsync(user, roles.Except(current)));
    }

    public async Task SetDisabledAsync(Guid userId, bool disabled, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        user.IsDisabled = disabled;
        Check(await users.UpdateAsync(user));
        // A new security stamp invalidates anything derived from the old one.
        Check(await users.UpdateSecurityStampAsync(user));
    }

    public async Task SetNameAsync(Guid userId, string firstName, string lastName, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        user.FirstName = firstName;
        user.LastName = lastName;
        Check(await users.UpdateAsync(user));
    }

    public async Task RecordSignInAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        user.LastSignInAtUtc = clock.UtcNow;
        Check(await users.UpdateAsync(user));
    }

    public async Task<(string SharedKey, string AuthenticatorUri)> GetAuthenticatorSetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        var key = await users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            Check(await users.ResetAuthenticatorKeyAsync(user));
            key = await users.GetAuthenticatorKeyAsync(user);
        }

        var uri = $"otpauth://totp/{UrlEncoder.Default.Encode(AuthenticatorIssuer)}:{UrlEncoder.Default.Encode(user.Email!)}?secret={key}&issuer={UrlEncoder.Default.Encode(AuthenticatorIssuer)}&digits=6";
        return (FormatKey(key!), uri);
    }

    public async Task<bool> VerifyAuthenticatorCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        if (await users.IsLockedOutAsync(user)) return false;
        if (await users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, code))
        {
            await users.ResetAccessFailedCountAsync(user);
            return true;
        }

        await users.AccessFailedAsync(user);
        return false;
    }

    public async Task<bool> RedeemRecoveryCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        if (await users.IsLockedOutAsync(user)) return false;
        var result = await users.RedeemTwoFactorRecoveryCodeAsync(user, code.Trim().ToUpperInvariant());
        if (result.Succeeded) return true;
        await users.AccessFailedAsync(user);
        return false;
    }

    public async Task<IReadOnlyList<string>> GenerateRecoveryCodesAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount) ?? []).Select(c => c.ToUpperInvariant()).ToList();
    }

    public async Task SetTwoFactorEnabledAsync(Guid userId, bool enabled, CancellationToken cancellationToken = default)
    {
        var user = await Require(userId);
        Check(await users.SetTwoFactorEnabledAsync(user, enabled));
        if (!enabled)
        {
            // A fresh key, so the old phone's codes stop working.
            Check(await users.ResetAuthenticatorKeyAsync(user));
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        }
    }

    private async Task<AppUser> Require(Guid userId) => await users.FindByIdAsync(userId.ToString()) ?? throw new NotFoundException("Account not found.");

    private async Task<UserAccount> ToAccountAsync(AppUser user) => ToAccount(user, (await users.GetRolesAsync(user)).Order().ToList());

    private static UserAccount ToAccount(AppUser u, IReadOnlyList<string> roles) =>
        new(u.Id, u.Email!, u.FirstName, u.LastName, u.EmailConfirmed, u.TwoFactorEnabled, u.IsDisabled, u.PasswordHash is not null, roles, u.CreatedAtUtc, u.LastSignInAtUtc);

    private static string FormatKey(string key) =>
        string.Join(' ', Enumerable.Range(0, (key.Length + 3) / 4).Select(i => key.Substring(i * 4, Math.Min(4, key.Length - i * 4)))).ToLowerInvariant();

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new DomainException(string.Join(" ", result.Errors.Select(e => e.Description)));
    }
}
