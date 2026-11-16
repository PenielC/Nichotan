using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nichotan.Api.Security;
using Nichotan.Application.Identity;
using Nichotan.Application.Staff;

namespace Nichotan.Api.Controllers;

public record RegisterRequest(string FirstName, string LastName, string Email, string Password, string? Phone);
public record LoginRequest(string Email, string Password);
public record MfaLoginRequest(string ChallengeToken, string Code, bool IsRecoveryCode = false);
public record TokenRequest(string Token);
public record EmailRequest(string Email);
public record ResetPasswordRequest(string Token, string NewPassword);
public record AcceptInvitationRequest(string Token, string Password);
public record CodeRequest(string Code);

public record SessionUser(Guid Id, string Email, string FirstName, string LastName, IReadOnlyList<string> Roles, bool IsStaff, bool EmailVerified, bool MfaEnabled);

/// <summary>A signed-in session. The refresh token is not in the body: it's set as an HttpOnly cookie.</summary>
public record SessionResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, SessionUser User, bool MfaSatisfied, bool MfaSetupRequired);

/// <summary>Sign-in result: a session, or a request for an authenticator code.</summary>
public record LoginResponse(bool MfaRequired, string? MfaChallengeToken, SessionResponse? Session);

[ApiController]
[Route("api/auth")]
public class AuthController(ISender sender) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("register")]
    public async Task<SessionResponse> Register(RegisterRequest request, CancellationToken cancellationToken) =>
        Start(await sender.Send(new RegisterCandidateCommand(request.FirstName, request.LastName, request.Email, request.Password, request.Phone), cancellationToken));

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("login")]
    public async Task<LoginResponse> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return result.MfaRequired ? new LoginResponse(true, result.MfaChallengeToken, null) : new LoginResponse(false, null, Start(result.Session!));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("login/mfa")]
    public async Task<SessionResponse> CompleteMfaLogin(MfaLoginRequest request, CancellationToken cancellationToken) =>
        Start(await sender.Send(new CompleteMfaLoginCommand(request.ChallengeToken, request.Code, request.IsRecoveryCode), cancellationToken));

    /// <summary>Exchanges the refresh cookie for a new access token (and a new cookie). The custom header blocks cross-site form posts.</summary>
    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<ActionResult<SessionResponse>> Refresh(CancellationToken cancellationToken)
    {
        if (!Request.Headers.ContainsKey("X-Nichotan-Client")) return BadRequest();
        var token = Request.Cookies[RefreshCookie.Name];
        if (string.IsNullOrEmpty(token)) return Unauthorized();
        return Start(await sender.Send(new RefreshSessionCommand(token), cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(Request.Cookies[RefreshCookie.Name]), cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(TokenRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new VerifyEmailCommand(request.Token), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.SignedIn)]
    [EnableRateLimiting(RateLimits.Email)]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(CancellationToken cancellationToken)
    {
        await sender.Send(new ResendVerificationCommand(), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Email)]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("accept-invitation")]
    public async Task<IActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new AcceptInvitationCommand(request.Token, request.Password), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("me")]
    public async Task<MeResult> Me(CancellationToken cancellationToken) => await sender.Send(new GetMeQuery(), cancellationToken);

    // ─────────────────────────── Authenticator app ───────────────────────────

    [Authorize(Policy = Policies.SignedIn)]
    [HttpGet("mfa/setup")]
    public async Task<MfaSetupResult> MfaSetup(CancellationToken cancellationToken) => await sender.Send(new GetMfaSetupQuery(), cancellationToken);

    /// <summary>Turns on the authenticator. The current session stays without amr=mfa: staff sign in again to use staff features.</summary>
    [Authorize(Policy = Policies.SignedIn)]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("mfa/enable")]
    public async Task<RecoveryCodesResult> EnableMfa(CodeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new EnableMfaCommand(request.Code), cancellationToken);

    [Authorize(Policy = Policies.SignedIn)]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("mfa/disable")]
    public async Task<IActionResult> DisableMfa(CodeRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new DisableMfaCommand(request.Code), cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.SignedIn)]
    [EnableRateLimiting(RateLimits.Auth)]
    [HttpPost("mfa/recovery-codes")]
    public async Task<RecoveryCodesResult> RegenerateRecoveryCodes(CodeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new RegenerateRecoveryCodesCommand(request.Code), cancellationToken);

    private SessionResponse Start(AuthSession session)
    {
        RefreshCookie.Set(Response, session.RefreshToken, session.RefreshExpiresAtUtc);
        var u = session.User;
        return new SessionResponse(
            session.Access.Token,
            session.Access.ExpiresAtUtc,
            new SessionUser(u.Id, u.Email, u.FirstName, u.LastName, u.Roles, u.IsStaff, u.EmailConfirmed, u.TwoFactorEnabled),
            session.MfaSatisfied,
            session.MfaSetupRequired);
    }
}

public static class RateLimits
{
    /// <summary>Sign-in and account forms: 10 requests a minute per IP address.</summary>
    public const string Auth = "auth";

    /// <summary>Endpoints that send email: 5 requests per 15 minutes per IP address.</summary>
    public const string Email = "email";
}
