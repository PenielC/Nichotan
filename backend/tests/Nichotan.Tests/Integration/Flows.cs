using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

/// <summary>Common journeys reused by the tests.</summary>
public static class Flows
{
    private static int _seq;

    public static string NewEmail(string prefix = "user") => $"{prefix}{Interlocked.Increment(ref _seq)}.{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 30)] + "@example.test";

    public static async Task<(HttpClient Client, SessionDto Session, string Email, string Password)> RegisterCandidateAsync(ApiFactory api, string? email = null)
    {
        var client = api.NewClient();
        email ??= NewEmail("cand");
        const string password = "Candidate-Pass-2026";
        var session = await (await client.PostAsJsonAsync("/api/auth/register", new { firstName = "Test", lastName = "Candidate", email, password })).Read<SessionDto>();
        client.UseToken(session.AccessToken);
        return (client, session, email, password);
    }

    public static async Task<LoginDto> LoginAsync(HttpClient client, string email, string password) =>
        await (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).Read<LoginDto>();

    /// <summary>Signs a staff member in all the way to an MFA session, setting up their authenticator first if needed.</summary>
    public static async Task<(HttpClient Client, SessionDto Session, string SharedKey)> StaffSignInAsync(ApiFactory api, string email, string password, string? sharedKey = null)
    {
        var client = api.NewClient();
        var login = await LoginAsync(client, email, password);
        if (!login.MfaRequired)
        {
            login.Session!.MfaSetupRequired.Should().BeTrue("staff without an authenticator must set one up");
            client.UseToken(login.Session.AccessToken);
            var setup = await (await client.GetAsync("/api/auth/mfa/setup")).Read<MfaSetupDto>();
            sharedKey = setup.SharedKey;
            await (await client.PostAsJsonAsync("/api/auth/mfa/enable", new { code = Totp.Code(sharedKey) })).Read<RecoveryCodesDto>();
            login = await LoginAsync(client, email, password);
        }

        login.MfaRequired.Should().BeTrue();
        var session = await (await client.PostAsJsonAsync("/api/auth/login/mfa", new { challengeToken = login.MfaChallengeToken, code = Totp.Code(sharedKey!) })).Read<SessionDto>();
        client.UseToken(session.AccessToken);
        return (client, session, sharedKey!);
    }

    private static string? _adminKey;
    private static readonly SemaphoreSlim AdminLock = new(1, 1);

    /// <summary>The bootstrapped administrator, signed in with MFA (the authenticator is set up once per run).</summary>
    public static async Task<HttpClient> AdminAsync(ApiFactory api)
    {
        await AdminLock.WaitAsync();
        try
        {
            var (client, _, key) = await StaffSignInAsync(api, ApiFactory.AdminEmail, ApiFactory.AdminPassword, _adminKey);
            _adminKey = key;
            // Codes can't be reused within the same 30s window by some apps; Identity accepts them, which keeps tests fast.
            return client;
        }
        finally
        {
            AdminLock.Release();
        }
    }

    /// <summary>Invites a staff member, accepts the invitation and signs them in with MFA.</summary>
    public static async Task<(HttpClient Client, StaffDto Staff, string Email, string Password)> NewStaffAsync(ApiFactory api, HttpClient admin, params string[] roles)
    {
        var email = NewEmail("staff");
        const string password = "Staff-Member-Pass-1";
        var staff = await (await admin.PostAsJsonAsync("/api/staff", new { email, firstName = "Staff", lastName = "Member", roles })).Read<StaffDto>();
        var token = api.Emails.LinkToken(email, "/accept-invitation");
        (await api.NewClient().PostAsJsonAsync("/api/auth/accept-invitation", new { token, password })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var (client, _, _) = await StaffSignInAsync(api, email, password);
        return (client, staff, email, password);
    }
}
