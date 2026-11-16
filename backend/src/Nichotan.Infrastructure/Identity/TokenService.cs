using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Nichotan.Application.Common;

namespace Nichotan.Infrastructure.Identity;

public class JwtOptions
{
    public string Issuer { get; set; } = "Nichotan";
    public string Audience { get; set; } = "Nichotan";

    /// <summary>HMAC signing key, at least 32 characters. Set from a secret store, never committed.</summary>
    public string Secret { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
}

/// <summary>Claim names used across the API.</summary>
public static class AppClaims
{
    /// <summary>"Authentication method reference": "mfa" when the sign-in passed a second factor.</summary>
    public const string Amr = "amr";
    public const string Mfa = "mfa";

    /// <summary>Marks the short-lived token that only allows completing a two-step sign-in.</summary>
    public const string MfaChallengeAudience = "Nichotan.MfaChallenge";
}

public class TokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    private static readonly TimeSpan MfaChallengeLifetime = TimeSpan.FromMinutes(5);
    private readonly JwtOptions _options = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions options)
    {
        if (options.Secret.Length < 32) throw new InvalidOperationException("Jwt:Secret must be at least 32 characters.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Secret));
    }

    public AccessToken CreateAccessToken(UserAccount user, bool mfaSatisfied)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
        if (mfaSatisfied) claims.Add(new Claim(AppClaims.Amr, AppClaims.Mfa));

        var expires = clock.UtcNow.AddMinutes(_options.AccessTokenMinutes);
        return new AccessToken(Write(claims, _options.Audience, expires), expires);
    }

    public string CreateMfaChallengeToken(Guid userId) =>
        Write([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], AppClaims.MfaChallengeAudience, clock.UtcNow.Add(MfaChallengeLifetime));

    public Guid? ReadMfaChallengeToken(string token)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = _options.Issuer,
                ValidAudience = AppClaims.MfaChallengeAudience,
                IssuerSigningKey = SigningKey(_options),
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);
            return Guid.TryParse(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string Write(IEnumerable<Claim> claims, string audience, DateTime expiresUtc)
    {
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: audience,
            claims: claims,
            notBefore: clock.UtcNow,
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
