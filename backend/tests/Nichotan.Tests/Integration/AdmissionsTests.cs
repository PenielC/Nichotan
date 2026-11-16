using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AdmissionsTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;
    private static readonly string Signature = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[300]]);

    private static object FormData() => new
    {
        personal = new { firstName = "Tanaka", lastName = "Moyo", gender = "Female", birthDate = "2004-05-17", citizenshipCountry = "Zimbabwe" },
        contact = new { homeAddress = "1 Test Road", city = "Harare", country = "Zimbabwe", email = "t@example.test", phone = "+263770000000", nextOfKinName = "Rudo", nextOfKinEmail = "r@example.test" },
        program = new { destinationCountry = "Canada", programLevel = "Bachelors", startYear = Year, startTerm = "Fall", institutions = new[] { "UofT" }, programChoices = new[] { "CS" } },
        academic = new { highSchools = new[] { new { name = "Harare High", provinceCountry = "Harare, Zimbabwe", started = "2016-01", completed = "2021-11", grade = "TwelveOrEquivalent" } }, postSecondary = Array.Empty<object>() },
    };

    private static async Task SubmitFormAsync(HttpClient candidate)
    {
        (await candidate.PutAsJsonAsync("/api/application-form", FormData())).StatusCode.Should().Be(HttpStatusCode.OK);
        (await candidate.PostAsJsonAsync("/api/application-form/submit", new { signerName = "Tanaka Moyo", signatureImage = Signature, acceptDeclaration = true, declarationVersion = "1" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(HttpClient Counsellor, HttpClient Admin, Guid ProgrammeId, Guid OtherProgrammeId)> SetUpAsync()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "University " + Guid.NewGuid().ToString("N")[..8], country = "Canada", website = "https://uni.example.test" })).Read<JsonElement>();
        var programmeId = await NewProgrammeAsync(counsellor, institution.GetProperty("id").GetGuid(), "Computer Science");
        var otherId = await NewProgrammeAsync(counsellor, institution.GetProperty("id").GetGuid(), "Economics");
        return (counsellor, admin, programmeId, otherId);
    }

    private static async Task<Guid> NewProgrammeAsync(HttpClient staff, Guid institutionId, string name)
    {
        var programme = await (await staff.PostAsJsonAsync("/api/programmes", new { institutionId, name, level = "Undergraduate", intakes = new[] { new { term = "Fall", year = Year }, new { term = "Winter", year = Year + 1 } } })).Read<JsonElement>();
        return programme.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> CreateAsync(HttpClient staff, Guid candidateId, Guid programmeId, string term = "Fall", int? year = null) =>
        await (await staff.PostAsJsonAsync("/api/admissions", new { candidateId, programmeId, intakeTerm = term, intakeYear = year ?? Year })).Read<JsonElement>();

    private static Task<HttpResponseMessage> MoveAsync(HttpClient staff, Guid id, string to, string? note = null, string? occurredOn = null) =>
        staff.PostAsJsonAsync($"/api/admissions/{id}/status", new { toStatus = to, note, occurredOn, acknowledgeDocumentGaps = true });

    private static async Task<Guid> CandidateIdAsync(HttpClient candidate) => (await (await candidate.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

    [Fact]
    public async Task The_dashboard_counts_open_applications_and_offers_not_finished_ones()
    {
        var (counsellor, admin, programmeId, otherProgrammeId) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (visa, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var candidateId = await CandidateIdAsync(candidate);
        async Task<(int Active, int Offers, int Total)> Stats()
        {
            var s = await (await visa.GetAsync("/api/admissions/stats")).Read<JsonElement>();
            return (s.GetProperty("active").GetInt32(), s.GetProperty("offers").GetInt32(), s.GetProperty("total").GetInt32());
        }

        var before = await Stats();
        var first = (await CreateAsync(counsellor, candidateId, programmeId)).GetProperty("summary").GetProperty("id").GetGuid();
        var second = (await CreateAsync(counsellor, candidateId, otherProgrammeId)).GetProperty("summary").GetProperty("id").GetGuid();
        (await Stats()).Should().Be((before.Active + 2, before.Offers, before.Total + 2));

        await MoveAsync(counsellor, first, "Withdrawn", "Changed their mind.");
        (await Stats()).Should().Be((before.Active + 1, before.Offers, before.Total + 2), "a withdrawn application is no longer open");

        await MoveAsync(counsellor, second, "ReadyForReview");
        await SubmitFormAsync(candidate);
        await MoveAsync(counsellor, second, "SubmittedToInstitution");
        await MoveAsync(counsellor, second, "OfferReceived", "Offer arrived.");
        (await Stats()).Should().Be((before.Active + 1, before.Offers + 1, before.Total + 2));

        // An offer still counts as issued after the application moves on.
        var issuedWithOffer = (await (await visa.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("offersIssued").GetInt32();
        await MoveAsync(counsellor, second, "Closed");
        var after = await (await visa.GetAsync("/api/admissions/stats")).Read<JsonElement>();
        after.GetProperty("offersIssued").GetInt32().Should().Be(issuedWithOffer);
        after.GetProperty("offers").GetInt32().Should().Be(before.Offers, "it is no longer sitting at offer received");

        (await candidate.GetAsync("/api/admissions/stats")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Counsellors_and_admins_maintain_the_directory_and_students_cannot()
    {
        var (counsellor, admin, _, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var body = new { name = "Directory Test " + Guid.NewGuid().ToString("N")[..8], country = "Germany" };

        (await candidate.GetAsync("/api/institutions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await candidate.PostAsJsonAsync("/api/institutions", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var created = await (await counsellor.PostAsJsonAsync("/api/institutions", body)).Read<JsonElement>();
        (await counsellor.PostAsJsonAsync("/api/institutions", body)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/institutions", new { name = "Another " + Guid.NewGuid().ToString("N")[..8], country = "Germany", website = "not a url" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var id = created.GetProperty("id").GetGuid();
        (await counsellor.PostAsJsonAsync($"/api/institutions/{id}/archive", new { archived = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        var visible = await (await counsellor.GetAsync("/api/institutions?country=Germany")).Read<JsonElement>();
        visible.EnumerateArray().Should().NotContain(i => i.GetProperty("id").GetGuid() == id, "archived institutions drop out of the default list");
        var all = await (await counsellor.GetAsync("/api/institutions?country=Germany&includeArchived=true")).Read<JsonElement>();
        all.EnumerateArray().Should().Contain(i => i.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task A_candidate_can_have_several_applications_but_not_two_open_ones_for_the_same_programme_and_intake()
    {
        var (counsellor, _, programmeId, otherProgrammeId) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);

        var first = await CreateAsync(counsellor, candidateId, programmeId);
        first.GetProperty("summary").GetProperty("status").GetString().Should().Be("Draft");
        first.GetProperty("history").GetArrayLength().Should().Be(1);
        await CreateAsync(counsellor, candidateId, otherProgrammeId);

        var duplicate = await counsellor.PostAsJsonAsync("/api/admissions", new { candidateId, programmeId, intakeTerm = "Fall", intakeYear = Year });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await counsellor.PostAsJsonAsync("/api/admissions", new { candidateId, programmeId, intakeTerm = "Summer", intakeYear = Year })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "the programme doesn't start in that intake");
        await CreateAsync(counsellor, candidateId, programmeId, "Winter", Year + 1);

        var list = await (await counsellor.GetAsync($"/api/admissions/candidate/{candidateId}")).Read<JsonElement>();
        list.GetArrayLength().Should().Be(3);
    }

    [Fact]
    public async Task Status_changes_follow_the_workflow_and_are_recorded_with_who_when_and_the_date_it_happened()
    {
        var (counsellor, _, programmeId, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);
        var id = (await CreateAsync(counsellor, candidateId, programmeId)).GetProperty("summary").GetProperty("id").GetGuid();

        (await MoveAsync(counsellor, id, "OfferReceived")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a draft can't jump to an offer");
        (await MoveAsync(counsellor, id, "ReadyForReview")).StatusCode.Should().Be(HttpStatusCode.OK);

        var notSigned = await MoveAsync(counsellor, id, "SubmittedToInstitution");
        notSigned.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await notSigned.Content.ReadAsStringAsync()).Should().Contain("signed and submitted");

        await SubmitFormAsync(candidate);
        var submitted = await (await MoveAsync(counsellor, id, "SubmittedToInstitution", occurredOn: DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"))).Read<JsonElement>();
        submitted.GetProperty("summary").GetProperty("submittedToInstitutionOn").GetString().Should().Be(DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"));

        (await MoveAsync(counsellor, id, "Rejected")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a rejection needs a note");
        (await MoveAsync(counsellor, id, "OfferReceived", occurredOn: DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "nothing can have happened in the future");
        var offer = await (await MoveAsync(counsellor, id, "OfferReceived", "Offer arrived by email.")).Read<JsonElement>();

        var history = offer.GetProperty("history").EnumerateArray().ToList();
        history.Select(h => h.GetProperty("to").GetString()).Should().Equal("Draft", "ReadyForReview", "SubmittedToInstitution", "OfferReceived");
        history[2].GetProperty("occurredOn").GetString().Should().Be(DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"));
        history[3].GetProperty("recordedBy").GetString().Should().Contain("@");
        offer.GetProperty("allowedMoves").EnumerateArray().Select(m => m.GetProperty("status").GetString()).Should().BeEquivalentTo("Closed", "Withdrawn");
    }

    [Fact]
    public async Task Only_admins_can_correct_a_wrong_status_and_the_original_entry_stays_in_the_history()
    {
        var (counsellor, admin, programmeId, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var id = (await CreateAsync(counsellor, await CandidateIdAsync(candidate), programmeId)).GetProperty("summary").GetProperty("id").GetGuid();
        (await MoveAsync(counsellor, id, "Withdrawn", "Recorded by mistake.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MoveAsync(counsellor, id, "Draft")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "withdrawn is final for normal moves");

        var attempt = new { toStatus = "ReadyForReview", note = "Withdrawn in error." };
        (await counsellor.PostAsJsonAsync($"/api/admissions/{id}/correction", attempt)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsJsonAsync($"/api/admissions/{id}/correction", new { toStatus = "ReadyForReview", note = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var fixedUp = await (await admin.PostAsJsonAsync($"/api/admissions/{id}/correction", attempt)).Read<JsonElement>();
        fixedUp.GetProperty("summary").GetProperty("status").GetString().Should().Be("ReadyForReview");
        var history = fixedUp.GetProperty("history").EnumerateArray().ToList();
        history.Select(h => h.GetProperty("to").GetString()).Should().Equal("Draft", "Withdrawn", "ReadyForReview");
        history[2].GetProperty("isCorrection").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_school_reference_and_portal_link_are_kept_and_candidates_see_only_their_own_applications_without_staff_notes()
    {
        var (counsellor, _, programmeId, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var id = (await CreateAsync(counsellor, await CandidateIdAsync(candidate), programmeId)).GetProperty("summary").GetProperty("id").GetGuid();

        (await counsellor.PutAsJsonAsync($"/api/admissions/{id}/details", new { institutionReference = "APP-12345", portalUrl = "javascript:alert(1)" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var detail = await (await counsellor.PutAsJsonAsync($"/api/admissions/{id}/details", new { institutionReference = "APP-12345", portalUrl = "https://apply.uni.example.test/app/1" })).Read<JsonElement>();
        detail.GetProperty("summary").GetProperty("institutionReference").GetString().Should().Be("APP-12345");
        detail.GetProperty("portalUrl").GetString().Should().StartWith("https://");

        await MoveAsync(counsellor, id, "Withdrawn", "Candidate chose another school, internal note.");

        var mine = await (await candidate.GetAsync("/api/admissions/mine")).Read<JsonElement>();
        mine.GetArrayLength().Should().Be(1);
        mine[0].GetProperty("status").GetString().Should().Be("Withdrawn");
        mine.GetRawText().Should().NotContain("internal note").And.NotContain("recordedBy").And.NotContain("APP-12345");
        (await (await other.GetAsync("/api/admissions/mine")).Read<JsonElement>()).GetArrayLength().Should().Be(0);

        (await candidate.GetAsync($"/api/admissions/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await candidate.GetAsync("/api/admissions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync("/api/admissions/mine")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
