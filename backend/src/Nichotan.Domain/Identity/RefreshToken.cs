using Nichotan.Domain.Common;

namespace Nichotan.Domain.Identity;

/// <summary>
/// A rotating refresh token (stored hashed). Each use replaces it with a new one in the same family;
/// presenting an already-replaced token means it was copied, so the whole family is revoked.
/// </summary>
public class RefreshToken : Entity
{
    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, Guid familyId, DateTime expiresAtUtc, bool mfaSatisfied)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        ExpiresAtUtc = expiresAtUtc;
        MfaSatisfied = mfaSatisfied;
    }

    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid FamilyId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public Guid? ReplacedById { get; private set; }

    /// <summary>Whether the sign-in this token belongs to passed a second factor (carried over on refresh).</summary>
    public bool MfaSatisfied { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    public void Revoke(DateTime nowUtc, Guid? replacedBy = null)
    {
        RevokedAtUtc ??= nowUtc;
        ReplacedById ??= replacedBy;
    }
}
