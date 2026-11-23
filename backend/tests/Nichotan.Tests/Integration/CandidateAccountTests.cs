using System.Net;
using Nichotan.Application.Common;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class CandidateAccountTests(ApiFactory api)
{
    [Fact]
    public async Task Registering_creates_a_candidate_with_a_profile_a_session_and_a_verification_email()
    {
        var (client, session, email, _) = await Flows.RegisterCandidateAsync(api);

        session.User.Roles.Should().Equal("Candidate");
        session.User.EmailVerified.Should().BeFalse();
        session.MfaSetupRequired.Should().BeFalse("candidates aren't required to use an authenticator");

        var me = await (await client.GetAsync("/api/auth/me")).Read<MeDto>();
        me.CandidateProfileId.Should().NotBeNull();
        var profile = await (await client.GetAsync("/api/candidates/me")).Read<ProfileDto>();
        profile.Email.Should().Be(email);
        profile.Id.Should().Be(me.CandidateProfileId!.Value);
        api.Emails.Sent.Should().Contain(m => m.To == email && m.Subject.Contains("Confirm your email") && m.From == EmailSenderKind.Automated, "an account link is system mail");
    }

    [Fact]
    public async Task Registration_sets_the_refresh_token_as_a_secure_http_only_cookie_only()
    {
        var client = api.NewClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { firstName = "A", lastName = "B", email = Flows.NewEmail(), password = "Candidate-Pass-2026" });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("nichotan_refresh="));
        cookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("secure").And.ContainEquivalentOf("samesite=strict").And.Contain("path=/api/auth");
        (await response.Content.ReadAsStringAsync()).Should().NotContainEquivalentOf("refreshToken");
    }

    [Fact]
    public async Task The_verification_link_confirms_the_email_once()
    {
        var (client, _, email, _) = await Flows.RegisterCandidateAsync(api);
        var token = api.Emails.LinkToken(email, "/verify-email");

        (await client.PostAsJsonAsync("/api/auth/verify-email", new { token })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await (await client.GetAsync("/api/auth/me")).Read<MeDto>()).EmailVerified.Should().BeTrue();

        var again = await client.PostAsJsonAsync("/api/auth/verify-email", new { token });
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_second_account_with_the_same_email_is_refused_whatever_the_case()
    {
        var (_, _, email, _) = await Flows.RegisterCandidateAsync(api);
        var response = await api.NewClient().PostAsJsonAsync("/api/auth/register", new { firstName = "A", lastName = "B", email = email.ToUpperInvariant(), password = "Candidate-Pass-2026" });
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Weak_passwords_and_invalid_fields_are_refused()
    {
        var client = api.NewClient();
        (await client.PostAsJsonAsync("/api/auth/register", new { firstName = "A", lastName = "B", email = Flows.NewEmail(), password = "short" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var noDigit = await client.PostAsJsonAsync("/api/auth/register", new { firstName = "A", lastName = "B", email = Flows.NewEmail(), password = "OnlyLettersHere" });
        noDigit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noDigit.Detail()).Should().Contain("digit");
        (await client.PostAsJsonAsync("/api/auth/register", new { firstName = "", lastName = "B", email = "not-an-email", password = "Candidate-Pass-2026" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_candidate_can_update_their_own_profile_and_it_is_audited()
    {
        var (client, session, _, _) = await Flows.RegisterCandidateAsync(api);
        var updated = await (await client.PutAsJsonAsync("/api/candidates/me", new { firstName = "Thandiwe", lastName = "Dube", phone = "+263 77 000 0000" })).Read<ProfileDto>();
        updated.FirstName.Should().Be("Thandiwe");
        var me = await (await client.GetAsync("/api/auth/me")).Read<MeDto>();
        (me.FirstName, me.LastName).Should().Be(("Thandiwe", "Dube"), "the account name follows the profile");

        var admin = await Flows.AdminAsync(api);
        var audit = await (await admin.GetAsync($"/api/audit-events?action=CandidateProfileUpdated&actorUserId={session.User.Id}")).Read<PageDto<AuditDto>>();
        audit.Items.Should().ContainSingle(e => e.EntityId == updated.Id.ToString());
    }

    [Fact]
    public async Task Password_reset_works_once_and_ends_existing_sessions()
    {
        var (client, _, email, _) = await Flows.RegisterCandidateAsync(api);
        var anonymous = api.NewClient();
        (await anonymous.PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var token = api.Emails.LinkToken(email, "/reset-password");

        (await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Brand-New-Pass-99" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anonymous.PostAsJsonAsync("/api/auth/reset-password", new { token, newPassword = "Another-Pass-1234" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // The old sign-in's refresh cookie no longer works.
        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        refresh.Headers.Add("X-Nichotan-Client", "web");
        (await client.SendAsync(refresh)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Flows.LoginAsync(api.NewClient(), email, "Brand-New-Pass-99")).Session.Should().NotBeNull();
        (await api.NewClient().PostAsJsonAsync("/api/auth/login", new { email, password = "Candidate-Pass-2026" })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Forgot_password_reveals_nothing_about_unknown_emails()
    {
        var email = Flows.NewEmail("nobody");
        (await api.NewClient().PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        api.Emails.Sent.Should().NotContain(m => m.To == email);
    }
}
