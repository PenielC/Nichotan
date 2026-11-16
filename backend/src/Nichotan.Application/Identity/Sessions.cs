using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Identity;

/// <summary>Sign-in failed. Mapped to 401 with a deliberately vague message.</summary>
public class AuthenticationFailedException(string message = "Email or password is incorrect.") : Exception(message);

/// <summary>Too many failed attempts. Mapped to 429.</summary>
public class AccountLockedException() : Exception("Too many failed attempts. Please wait 15 minutes and try again.");

public class SessionOptions
{
    public int RefreshTokenDays { get; set; } = 14;
}

/// <summary>A signed-in session: a short-lived access token and a rotating refresh token (sent as an HttpOnly cookie).</summary>
public record AuthSession(AccessToken Access, string RefreshToken, DateTime RefreshExpiresAtUtc, UserAccount User, bool MfaSatisfied, bool MfaSetupRequired);

/// <summary>Either a session, or (when the account has an authenticator) a challenge to complete with a code.</summary>
public record LoginResult(AuthSession? Session, string? MfaChallengeToken)
{
    public bool MfaRequired => MfaChallengeToken is not null;
}

/// <summary>Issues and rotates sessions. Callers save the unit of work.</summary>
public class SessionIssuer(IAppDbContext db, ITokenService tokens, IClock clock, IOptions<SessionOptions> sessionOptions, IOptions<SecurityOptions> security)
{
    public AuthSession Issue(UserAccount user, bool mfaSatisfied, Guid? familyId = null)
    {
        var plain = AuthToken.Base64Url(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        var expires = clock.UtcNow.AddDays(sessionOptions.Value.RefreshTokenDays);
        var refresh = new RefreshToken(user.Id, AuthToken.Hash(plain), familyId ?? Guid.NewGuid(), expires, mfaSatisfied);
        db.RefreshTokens.Add(refresh);
        LastIssued = refresh;
        return new AuthSession(tokens.CreateAccessToken(user, mfaSatisfied), plain, expires, user, mfaSatisfied, MfaSetupRequired(user));
    }

    /// <summary>The refresh token created by the last Issue call (so rotation can link old to new).</summary>
    public RefreshToken? LastIssued { get; private set; }

    /// <summary>Staff without an authenticator must set one up before using staff features.</summary>
    public bool MfaSetupRequired(UserAccount user) => security.Value.RequireMfaForStaff && user.IsStaff && !user.TwoFactorEnabled;

    /// <summary>Signs the user out everywhere (password reset, account disabled, refresh-token theft).</summary>
    public async Task RevokeAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, now), cancellationToken);
    }

    /// <summary>Invalidates earlier unused links of the same kind, so only the newest one works.</summary>
    public async Task<string> IssueLinkTokenAsync(Guid userId, AuthTokenPurpose purpose, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await db.AuthTokens.Where(t => t.UserId == userId && t.Purpose == purpose && t.UsedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAtUtc, now), cancellationToken);
        var (token, plain) = AuthToken.Issue(userId, purpose, lifetime, now);
        db.AuthTokens.Add(token);
        return plain;
    }

    public async Task<AuthToken> RedeemLinkTokenAsync(string plain, AuthTokenPurpose purpose, CancellationToken cancellationToken)
    {
        var hash = AuthToken.Hash(plain ?? string.Empty);
        var token = await db.AuthTokens.FirstOrDefaultAsync(t => t.TokenHash == hash && t.Purpose == purpose, cancellationToken);
        if (token is null || !token.IsUsable(clock.UtcNow))
        {
            throw new DomainException("This link is invalid or has expired. Please request a new one.");
        }

        token.MarkUsed(clock.UtcNow);
        return token;
    }
}

public static class LinkLifetimes
{
    public static readonly TimeSpan EmailVerification = TimeSpan.FromHours(48);
    public static readonly TimeSpan PasswordReset = TimeSpan.FromHours(1);
    public static readonly TimeSpan StaffInvitation = TimeSpan.FromHours(72);
}
