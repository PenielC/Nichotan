using System.Security.Cryptography;
using System.Text;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Identity;

public enum AuthTokenPurpose
{
    EmailVerification = 0,
    PasswordReset = 1,
    StaffInvitation = 2,
}

/// <summary>
/// A single-use link token (email verification, password reset, staff invitation). Only a SHA-256 hash
/// is stored, so a database leak doesn't hand out working links.
/// </summary>
public class AuthToken : Entity
{
    private AuthToken() { }

    public Guid UserId { get; private set; }
    public AuthTokenPurpose Purpose { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    /// <summary>Creates a token; returns the plain value (sent once, by email) alongside the entity.</summary>
    public static (AuthToken Token, string PlainValue) Issue(Guid userId, AuthTokenPurpose purpose, TimeSpan lifetime, DateTime nowUtc)
    {
        var plain = Base64Url(RandomNumberGenerator.GetBytes(32));
        return (new AuthToken { UserId = userId, Purpose = purpose, TokenHash = Hash(plain), ExpiresAtUtc = nowUtc.Add(lifetime) }, plain);
    }

    public static string Hash(string plain) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain)));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public bool IsUsable(DateTime nowUtc) => UsedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void MarkUsed(DateTime nowUtc) => UsedAtUtc ??= nowUtc;
}
