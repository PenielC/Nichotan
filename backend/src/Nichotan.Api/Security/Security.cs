using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Identity;
using Nichotan.Infrastructure.Identity;

namespace Nichotan.Api.Security;

/// <summary>Authorization policy names. Every protected endpoint uses one of these.</summary>
public static class Policies
{
    /// <summary>Any signed-in user (e.g. setting up an authenticator, reading their own account).</summary>
    public const string SignedIn = "SignedIn";

    public const string Candidate = "Candidate";

    /// <summary>Staff who work on individual cases (not report viewers).</summary>
    public const string CaseWorker = "CaseWorker";

    /// <summary>Administrators and admissions counsellors: the directory and admission applications.</summary>
    public const string AdmissionsStaff = "AdmissionsStaff";

    /// <summary>Administrators, counsellors and visa officers: opening and progressing visa cases.</summary>
    public const string VisaStaff = "VisaStaff";

    /// <summary>Anyone who may read reports: aggregates only.</summary>
    public const string Reports = "Reports";

    /// <summary>Administrators, counsellors and document reviewers: reviewing documents and uploading for candidates.</summary>
    public const string DocumentStaff = "DocumentStaff";

    /// <summary>A candidate, or staff who handle cases (the handler then limits a candidate to their own records).</summary>
    public const string CandidateOrStaff = "CandidateOrStaff";

    public const string Administrator = "Administrator";

    public static void Configure(AuthorizationOptions options, bool requireMfaForStaff)
    {
        // Staff tokens must carry amr=mfa when the setting is on: a password alone doesn't open staff features.
        bool MfaOk(AuthorizationHandlerContext context) => !requireMfaForStaff || context.User.HasClaim(AppClaims.Amr, AppClaims.Mfa);

        options.AddPolicy(SignedIn, p => p.RequireAuthenticatedUser());
        options.AddPolicy(Candidate, p => p.RequireAuthenticatedUser().RequireRole(Roles.Candidate));
        options.AddPolicy(CaseWorker, p => p.RequireAuthenticatedUser().RequireRole(Guard.CaseWorkers).RequireAssertion(MfaOk));
        options.AddPolicy(AdmissionsStaff, p => p.RequireAuthenticatedUser().RequireRole(Guard.AdmissionsStaff).RequireAssertion(MfaOk));
        options.AddPolicy(Reports, p => p.RequireAuthenticatedUser().RequireRole(Guard.ReportStaff).RequireAssertion(MfaOk));
        options.AddPolicy(VisaStaff, p => p.RequireAuthenticatedUser().RequireRole(Guard.VisaStaff).RequireAssertion(MfaOk));
        options.AddPolicy(DocumentStaff, p => p.RequireAuthenticatedUser().RequireRole(Guard.DocumentStaff).RequireAssertion(MfaOk));
        options.AddPolicy(CandidateOrStaff, p => p.RequireAuthenticatedUser().RequireAssertion(c => c.User.IsInRole(Roles.Candidate) || (Guard.CaseWorkers.Any(c.User.IsInRole) && MfaOk(c))));
        options.AddPolicy(Administrator, p => p.RequireAuthenticatedUser().RequireRole(Roles.Administrator).RequireAssertion(MfaOk));
        // Endpoints without an explicit policy still need a signed-in user.
        options.FallbackPolicy = options.GetPolicy(SignedIn);
    }
}

/// <summary>The signed-in user, read from the validated access token.</summary>
public class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;
    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);
    public IReadOnlyCollection<string> Roles => Principal?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? [];
    public bool MfaSatisfied => Principal?.HasClaim(AppClaims.Amr, AppClaims.Mfa) == true;
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
    public bool IsInRole(string role) => Principal?.IsInRole(role) == true;
}

/// <summary>Records refused requests from signed-in users (a 403), then lets the default handler respond.</summary>
public class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
        {
            var audit = context.RequestServices.GetRequiredService<IAuditLog>();
            var db = context.RequestServices.GetRequiredService<IAppDbContext>();
            audit.Add(AuditAction.AccessDenied, AuditOutcome.Denied, details: new { method = context.Request.Method, path = context.Request.Path.Value });
            await db.SaveChangesAsync(context.RequestAborted);
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

/// <summary>The refresh token lives in an HttpOnly cookie that only the auth endpoints receive.</summary>
public static class RefreshCookie
{
    public const string Name = "nichotan_refresh";
    public const string Path = "/api/auth";

    public static void Set(HttpResponse response, string token, DateTime expiresUtc) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            Expires = expiresUtc,
            IsEssential = true,
        });

    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = Path });
}
