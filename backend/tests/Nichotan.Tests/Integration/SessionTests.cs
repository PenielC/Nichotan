using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class SessionTests(ApiFactory api)
{
    private static HttpRequestMessage Refresh()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("X-Nichotan-Client", "web");
        return request;
    }

    [Fact]
    public async Task Wrong_passwords_give_one_vague_message_and_five_lock_the_account()
    {
        var (_, _, email, password) = await Flows.RegisterCandidateAsync(api);
        var client = api.NewClient();

        for (var i = 0; i < 4; i++)
        {
            var wrong = await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password-1" });
            wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await wrong.Detail()).Should().Be("Email or password is incorrect.");
        }

        (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Wrong-Password-1" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        // Locked: even the right password is refused for now.
        (await client.PostAsJsonAsync("/api/auth/login", new { email, password })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { email = Flows.NewEmail("ghost"), password });
        (await unknown.Detail()).Should().Be("Email or password is incorrect.", "unknown accounts look the same as wrong passwords");

        var admin = await Flows.AdminAsync(api);
        var audit = await (await admin.GetAsync("/api/audit-events?action=LoginLockedOut&pageSize=200")).Read<PageDto<AuditDto>>();
        audit.Items.Should().Contain(e => e.ActorEmail == email);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_a_replayed_token_ends_the_whole_session_family()
    {
        // Cookies handled by hand here, to play both the browser and a thief holding a copied cookie.
        var client = api.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
        var register = await client.PostAsJsonAsync("/api/auth/register", new { firstName = "A", lastName = "B", email = Flows.NewEmail(), password = "Candidate-Pass-2026" });
        var original = RefreshCookieOf(register);

        var rotate = Refresh();
        rotate.Headers.Add("Cookie", $"nichotan_refresh={original}");
        var rotated = await client.SendAsync(rotate);
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = RefreshCookieOf(rotated);
        current.Should().NotBe(original, "each refresh issues a new token");

        // The thief replays the original token: refused, and the family is revoked.
        var replay = Refresh();
        replay.Headers.Add("Cookie", $"nichotan_refresh={original}");
        (await client.SendAsync(replay)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // So the legitimate browser's newer token stops working too: both have to sign in again.
        var legit = Refresh();
        legit.Headers.Add("Cookie", $"nichotan_refresh={current}");
        (await client.SendAsync(legit)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var admin = await Flows.AdminAsync(api);
        var audit = await (await admin.GetAsync("/api/audit-events?action=RefreshTokenReuseDetected&pageSize=200")).Read<PageDto<AuditDto>>();
        audit.Items.Should().NotBeEmpty();
    }

    private static string RefreshCookieOf(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nichotan_refresh=")).Split(';')[0]["nichotan_refresh=".Length..];

    [Fact]
    public async Task Refresh_needs_the_client_header_and_a_cookie()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await client.PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Refresh().SendWith(api.NewClient())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await client.PostAsync("/api/auth/logout", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Refresh().SendWith(client)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_endpoints_need_a_valid_token()
    {
        var client = api.NewClient();
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.UseToken("not-a-token");
        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Health_checks_answer_without_signing_in()
    {
        var client = api.NewClient();
        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

internal static class RequestExtensions
{
    public static Task<HttpResponseMessage> SendWith(this HttpRequestMessage request, HttpClient client) => client.SendAsync(request);
}
