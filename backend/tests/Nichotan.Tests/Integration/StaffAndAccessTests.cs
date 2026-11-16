using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class StaffAndAccessTests(ApiFactory api)
{
    [Fact]
    public async Task Staff_need_an_authenticator_before_staff_features_open()
    {
        // A newly invited counsellor, signed in with just their password.
        var admin = await Flows.AdminAsync(api);
        var email = Flows.NewEmail("newstaff");
        await (await admin.PostAsJsonAsync("/api/staff", new { email, firstName = "New", lastName = "Staff", roles = new[] { "AdmissionsCounsellor" } })).Read<StaffDto>();
        var token = api.Emails.LinkToken(email, "/accept-invitation");
        (await api.NewClient().PostAsJsonAsync("/api/auth/accept-invitation", new { token, password = "Staff-Member-Pass-1" })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var client = api.NewClient();
        var login = await Flows.LoginAsync(client, email, "Staff-Member-Pass-1");
        login.MfaRequired.Should().BeFalse();
        login.Session!.MfaSetupRequired.Should().BeTrue();
        login.Session.MfaSatisfied.Should().BeFalse();
        client.UseToken(login.Session.AccessToken);

        (await client.GetAsync("/api/candidates")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "a password alone doesn't open staff features");
        (await client.GetAsync("/api/auth/mfa/setup")).StatusCode.Should().Be(HttpStatusCode.OK, "but setting up the authenticator is allowed");
        (await (await client.GetAsync("/api/auth/me")).Read<MeDto>()).MfaSetupRequired.Should().BeTrue();

        // Staff can't switch their authenticator off while the setting requires it.
        var (staffClient, _, key) = await Flows.StaffSignInAsync(api, email, "Staff-Member-Pass-1");
        var disable = await staffClient.PostAsJsonAsync("/api/auth/mfa/disable", new { code = Totp.Code(key) });
        disable.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sign_in_with_mfa_opens_staff_features_and_a_wrong_code_does_not()
    {
        var admin = await Flows.AdminAsync(api);
        (await admin.GetAsync("/api/staff")).StatusCode.Should().Be(HttpStatusCode.OK);

        var client = api.NewClient();
        var login = await Flows.LoginAsync(client, ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        login.MfaRequired.Should().BeTrue();
        login.Session.Should().BeNull("no session until the second step is done");
        var wrong = await client.PostAsJsonAsync("/api/auth/login/mfa", new { challengeToken = login.MfaChallengeToken, code = "000000" });
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var forged = await client.PostAsJsonAsync("/api/auth/login/mfa", new { challengeToken = "forged.token.value", code = "123456" });
        forged.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_invited_counsellor_sets_a_password_sets_up_mfa_and_sees_candidates_but_not_admin_areas()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, staff, email, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        staff.InvitationPending.Should().BeTrue();

        (await counsellor.GetAsync("/api/candidates")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await counsellor.GetAsync("/api/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await counsellor.GetAsync("/api/audit-events")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var list = await (await admin.GetAsync("/api/staff")).Read<List<StaffDto>>();
        list.Single(s => s.Email == email).Should().Match<StaffDto>(s => !s.InvitationPending && s.MfaEnabled);

        // The invitation link only works once.
        var token = api.Emails.LinkToken(email, "/accept-invitation");
        (await api.NewClient().PostAsJsonAsync("/api/auth/accept-invitation", new { token, password = "Another-Pass-123" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Role_changes_and_disabling_apply_to_tokens_already_issued()
    {
        var admin = await Flows.AdminAsync(api);
        var (staffClient, staff, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        (await staffClient.GetAsync("/api/candidates")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A counsellor can't manage staff accounts; once made an administrator, the same token can at once.
        (await staffClient.GetAsync("/api/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync($"/api/staff/{staff.Id}/roles", new { roles = new[] { "Administrator" } })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staffClient.GetAsync("/api/staff")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.PostAsync($"/api/staff/{staff.Id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staffClient.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var audit = await (await admin.GetAsync($"/api/audit-events?entityType=User&entityId={staff.Id}&pageSize=200")).Read<PageDto<AuditDto>>();
        audit.Items.Select(e => e.Action).Should().Contain(["StaffInvited", "StaffInvitationAccepted", "StaffRolesChanged", "StaffDisabled"]);
        audit.Items.Single(e => e.Action == "StaffRolesChanged").Details.Should().Contain("AdmissionsCounsellor").And.Contain("Administrator");
    }

    [Fact]
    public async Task Administrators_cannot_lock_themselves_or_nichotan_out()
    {
        var admin = await Flows.AdminAsync(api);
        var me = await (await admin.GetAsync("/api/auth/me")).Read<MeDto>();
        (await admin.PostAsync($"/api/staff/{me.Id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PutAsJsonAsync($"/api/staff/{me.Id}/roles", new { roles = new[] { "AdmissionsCounsellor" } })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/staff", new { email = Flows.NewEmail(), firstName = "X", lastName = "Y", roles = new[] { "Candidate" } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "staff can't be given the candidate role");
    }

    [Fact]
    public async Task Staff_are_only_administrators_or_counsellors_and_the_retired_roles_are_refused()
    {
        var admin = await Flows.AdminAsync(api);
        foreach (var retired in new[] { "VisaOfficer", "DocumentReviewer", "ReportViewer" })
        {
            (await admin.PostAsJsonAsync("/api/staff", new { email = Flows.NewEmail(), firstName = "X", lastName = "Y", roles = new[] { retired } })).StatusCode.Should().Be(HttpStatusCode.BadRequest, retired);
        }
    }

    [Fact]
    public async Task An_administrator_can_reset_a_lost_authenticator()
    {
        var admin = await Flows.AdminAsync(api);
        var (_, staff, email, password) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        (await admin.PostAsync($"/api/staff/{staff.Id}/reset-mfa", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var login = await Flows.LoginAsync(api.NewClient(), email, password);
        login.MfaRequired.Should().BeFalse();
        login.Session!.MfaSetupRequired.Should().BeTrue("they set up a new authenticator before working again");
    }

    [Fact]
    public async Task Candidates_reach_only_their_own_records()
    {
        var (alice, aliceSession, _, _) = await Flows.RegisterCandidateAsync(api);
        var (bob, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var bobProfile = await (await bob.GetAsync("/api/candidates/me")).Read<ProfileDto>();

        (await alice.GetAsync($"/api/candidates/{bobProfile.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync("/api/candidates")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync("/api/staff")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await alice.GetAsync("/api/audit-events")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await alice.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id.Should().NotBe(bobProfile.Id);

        var admin = await Flows.AdminAsync(api);
        var denied = await (await admin.GetAsync($"/api/audit-events?action=AccessDenied&actorUserId={aliceSession.User.Id}&pageSize=200")).Read<PageDto<AuditDto>>();
        denied.Items.Should().HaveCountGreaterThanOrEqualTo(4);
        denied.Items.Should().OnlyContain(e => e.Outcome == "Denied");
    }

    [Fact]
    public async Task Staff_can_search_candidates_and_opening_a_record_is_audited()
    {
        var (_, _, email, _) = await Flows.RegisterCandidateAsync(api);
        var admin = await Flows.AdminAsync(api);
        var found = await (await admin.GetAsync($"/api/candidates?search={Uri.EscapeDataString(email[..12])}")).Read<PageDto<ProfileDto>>();
        var hit = found.Items.Should().ContainSingle(c => c.Email == email).Subject;

        (await admin.GetAsync($"/api/candidates/{hit.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var audit = await (await admin.GetAsync($"/api/audit-events?action=CandidateProfileViewed&entityId={hit.Id}")).Read<PageDto<AuditDto>>();
        audit.Items.Should().ContainSingle();
        (await admin.GetAsync($"/api/candidates/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
