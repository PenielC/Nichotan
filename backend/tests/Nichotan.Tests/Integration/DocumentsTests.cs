using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class DocumentsTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;
    private static byte[] Pdf(string text = "x") => Encoding.ASCII.GetBytes("%PDF-1.4\n" + text + new string(' ', 200));

    private static async Task<JsonElement> OverviewAsync(HttpClient candidate) => await (await candidate.GetAsync("/api/documents/mine")).Read<JsonElement>();

    private static JsonElement Slot(JsonElement overview, string typeName, string? applicationId = null) =>
        overview.GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("typeName").GetString() == typeName && (applicationId is null || s.GetProperty("applicationId").GetString() == applicationId));

    private static async Task<Guid> TypeIdAsync(HttpClient staff, string name) =>
        (await (await staff.GetAsync("/api/document-types")).Read<JsonElement>()).EnumerateArray().Single(t => t.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid typeId, byte[] bytes, string fileName = "passport.pdf", Guid? applicationId = null, Guid? candidateId = null, string? expiresOn = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(typeId.ToString()), "DocumentTypeId" } };
        if (applicationId is { } app) form.Add(new StringContent(app.ToString()), "ApplicationId");
        if (candidateId is { } cand) form.Add(new StringContent(cand.ToString()), "CandidateId");
        if (expiresOn is not null) form.Add(new StringContent(expiresOn), "ExpiresOn");
        form.Add(new ByteArrayContent(bytes), "File", fileName);
        return client.PostAsync("/api/documents", form);
    }

    private static async Task<Guid> CandidateIdAsync(HttpClient candidate) => (await (await candidate.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

    private async Task<(HttpClient Admin, HttpClient Counsellor, HttpClient Reviewer, Guid InstitutionId, Guid UgProgramme, Guid PgProgramme)> SetUpAsync()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (reviewer, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "Docs University " + Guid.NewGuid().ToString("N")[..8], country = "Canada" })).Read<JsonElement>();
        var institutionId = institution.GetProperty("id").GetGuid();
        async Task<Guid> Programme(string name, string level) =>
            (await (await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId, name, level, intakes = new[] { new { term = "Fall", year = Year } } })).Read<JsonElement>()).GetProperty("id").GetGuid();
        return (admin, counsellor, reviewer, institutionId, await Programme("Business", "Undergraduate"), await Programme("Data Science", "Postgraduate"));
    }

    private static async Task<Guid> NewProgrammeAsync(HttpClient staff, Guid institutionId, string name, string level) =>
        (await (await staff.PostAsJsonAsync("/api/programmes", new { institutionId, name, level, intakes = new[] { new { term = "Fall", year = Year } } })).Read<JsonElement>()).GetProperty("id").GetGuid();

    private static async Task<Guid> NewApplicationAsync(HttpClient staff, Guid candidateId, Guid programmeId) =>
        (await (await staff.PostAsJsonAsync("/api/admissions", new { candidateId, programmeId, intakeTerm = "Fall", intakeYear = Year })).Read<JsonElement>()).GetProperty("summary").GetProperty("id").GetGuid();

    [Fact]
    public async Task Everyone_starts_with_the_always_required_documents_and_nothing_uploaded()
    {
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var overview = await OverviewAsync(candidate);

        overview.GetProperty("slots").EnumerateArray().Select(s => s.GetProperty("typeName").GetString()).Should().BeEquivalentTo("Passport", "Academic records", "Nichotan application form");
        Slot(overview, "Passport").GetProperty("status").GetString().Should().Be("Missing");
        Slot(overview, "Passport").GetProperty("required").GetBoolean().Should().BeTrue();
        Slot(overview, "Nichotan application form").GetProperty("isAutomatic").GetBoolean().Should().BeTrue();
    }

    private static object FormFor(string level) => new
    {
        personal = new { firstName = "Lee", lastName = "Level" },
        program = new { destinationCountry = "Canada", programLevel = level },
    };

    private static string[] Names(JsonElement overview) => overview.GetProperty("slots").EnumerateArray().Where(s => s.GetProperty("required").GetBoolean()).Select(s => s.GetProperty("typeName").GetString()!).ToArray();

    [Fact]
    public async Task The_documents_a_candidate_is_asked_for_follow_the_level_chosen_on_their_form()
    {
        var admin = await Flows.AdminAsync(api);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        string[] basics = ["Passport", "Academic records", "Nichotan application form"];

        Names(await OverviewAsync(candidate)).Should().BeEquivalentTo(basics, "no level chosen yet");

        await candidate.PutAsJsonAsync("/api/application-form", FormFor("Masters"));
        var postgraduate = Names(await OverviewAsync(candidate));
        postgraduate.Should().Contain(basics).And.Contain(["CV", "Academic reference", "Professional reference"]);
        postgraduate.Should().NotContain("Statement of purpose", "that one belongs to a particular application, so it waits for one");

        await candidate.PutAsJsonAsync("/api/application-form", FormFor("Bachelors"));
        Names(await OverviewAsync(candidate)).Should().BeEquivalentTo(basics, "undergraduates aren't asked for the postgraduate extras");

        // An administrator can add something for high school applicants only.
        var report = await (await admin.PostAsJsonAsync("/api/document-types", new { name = "School report " + Guid.NewGuid().ToString("N")[..6], scope = "Candidate", tracksExpiry = false })).Read<JsonElement>();
        await admin.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = report.GetProperty("id").GetGuid(), level = "HighSchool", isRequired = true });

        await candidate.PutAsJsonAsync("/api/application-form", FormFor("HighSchool"));
        Names(await OverviewAsync(candidate)).Should().Contain(report.GetProperty("name").GetString()!);
        await candidate.PutAsJsonAsync("/api/application-form", FormFor("Diploma"));
        Names(await OverviewAsync(candidate)).Should().NotContain(report.GetProperty("name").GetString()!);
    }

    [Fact]
    public async Task A_candidate_uploads_a_file_and_it_is_stored_privately_with_its_version()
    {
        var admin = await Flows.AdminAsync(api);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var passport = await TypeIdAsync(admin, "Passport");

        var response = await UploadAsync(candidate, passport, Pdf("scan"), "My Passport.pdf", expiresOn: DateTime.UtcNow.AddYears(3).ToString("yyyy-MM-dd"));
        var detail = await response.Read<JsonElement>();
        detail.GetProperty("status").GetString().Should().Be("Uploaded", "an upload alone is never verified");
        detail.GetProperty("versions")[0].GetProperty("fileName").GetString().Should().Be("My Passport.pdf");

        var slot = Slot(await OverviewAsync(candidate), "Passport");
        slot.GetProperty("status").GetString().Should().Be("Uploaded");
        slot.GetProperty("fileName").GetString().Should().Be("My Passport.pdf");
        slot.GetProperty("expiresOn").GetString().Should().NotBeNull();

        var id = detail.GetProperty("id").GetGuid();
        var file = await candidate.GetAsync($"/api/documents/{id}/file");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        (await file.Content.ReadAsByteArrayAsync()).Should().Equal(Pdf("scan"));
    }

    [Fact]
    public async Task Files_are_checked_by_content_size_and_type()
    {
        var admin = await Flows.AdminAsync(api);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var passport = await TypeIdAsync(admin, "Passport");
        var records = await TypeIdAsync(admin, "Academic records");

        (await UploadAsync(candidate, passport, Encoding.ASCII.GetBytes("just text, not a pdf at all"), "fake.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadAsync(candidate, passport, Pdf(), "program.exe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadAsync(candidate, passport, [], "empty.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadAsync(candidate, passport, [.. "%PDF-"u8.ToArray(), .. new byte[10 * 1024 * 1024]], "big.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await UploadAsync(candidate, records, Pdf(), "r.pdf", expiresOn: DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "only some documents expire");
        (await UploadAsync(candidate, passport, Pdf(), "old.pdf", expiresOn: "2001-01-01")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already expired");
        (await UploadAsync(candidate, passport, [0xFF, 0xD8, 0xFF, .. new byte[200]], "photo.JPG")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(candidate, await TypeIdAsync(admin, "Nichotan application form"), Pdf(), "f.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "met by signing the form");
        (await UploadAsync(candidate, await TypeIdAsync(admin, "CV"), Pdf(), "cv.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not on this candidate's list");
    }

    [Fact]
    public async Task Reviewing_is_separate_from_uploading_and_every_version_is_kept()
    {
        var (admin, _, reviewer, _, _, _) = await SetUpAsync();
        var (candidate, _, email, _) = await Flows.RegisterCandidateAsync(api);
        var passport = await TypeIdAsync(admin, "Passport");
        var id = (await (await UploadAsync(candidate, passport, Pdf("v1"))).Read<JsonElement>()).GetProperty("id").GetGuid();

        (await candidate.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Accepted" })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "candidates can't review");
        (await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "NeedsResubmission" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "say what to fix");
        (await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Uploaded" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var asked = await (await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "NeedsResubmission", note = "The photo page is cut off." })).Read<JsonElement>();
        asked.GetProperty("status").GetString().Should().Be("NeedsResubmission");
        api.Emails.Sent.Should().Contain(m => m.To == email && m.Subject.Contains("uploaded again"));

        var mine = Slot(await OverviewAsync(candidate), "Passport");
        mine.GetProperty("status").GetString().Should().Be("NeedsResubmission");
        mine.GetProperty("reviewNote").GetString().Should().Contain("cut off");

        var second = await (await UploadAsync(candidate, passport, Pdf("v2"))).Read<JsonElement>();
        second.GetProperty("currentVersion").GetInt32().Should().Be(2);
        second.GetProperty("status").GetString().Should().Be("Uploaded");
        (await (await candidate.GetAsync($"/api/documents/{id}/file?version=1")).Content.ReadAsByteArrayAsync()).Should().Equal(Pdf("v1"));

        var accepted = await (await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Accepted" })).Read<JsonElement>();
        accepted.GetProperty("reviews").GetArrayLength().Should().Be(2);
        (await UploadAsync(candidate, passport, Pdf("v3"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an accepted document is locked for the candidate");

        var asCandidate = await (await candidate.GetAsync($"/api/documents/{id}")).Read<JsonElement>();
        asCandidate.GetRawText().Should().NotContain("reviewer@").And.NotContain("@example.test\"", "staff identities stay hidden from candidates");
        asCandidate.GetProperty("reviews")[0].GetProperty("reviewer").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Staff_can_upload_on_a_candidates_behalf_and_only_the_right_people_can_read_files()
    {
        var (admin, counsellor, reviewer, _, _, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);
        var records = await TypeIdAsync(admin, "Academic records");

        (await UploadAsync(counsellor, records, Pdf("c"))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "staff must say which candidate");
        var detail = await (await UploadAsync(counsellor, records, Pdf("c"), candidateId: candidateId)).Read<JsonElement>();
        var id = detail.GetProperty("id").GetGuid();
        detail.GetProperty("versions")[0].GetProperty("uploadedBy").GetString().Should().Contain("@");

        (await reviewer.GetAsync($"/api/documents/{id}/file")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reviewer.GetAsync($"/api/documents/candidate/{candidateId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await other.GetAsync($"/api/documents/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound, "another candidate can't tell it exists");
        (await other.GetAsync($"/api/documents/{id}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/documents/candidate/{candidateId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UploadAsync(other, records, Pdf("x"), candidateId: candidateId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync($"/api/documents/{id}/file")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await (await reviewer.GetAsync("/api/documents/review-queue")).Read<JsonElement>()).GetProperty("items").EnumerateArray()
            .Should().Contain(i => i.GetProperty("documentId").GetGuid() == id);
    }

    [Fact]
    public async Task Postgraduate_applications_ask_for_more_and_shared_documents_are_uploaded_once()
    {
        var (admin, counsellor, _, _, ugProgramme, pgProgramme) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);

        var ug = await NewApplicationAsync(counsellor, candidateId, ugProgramme);
        var ugChecklist = await (await counsellor.GetAsync($"/api/admissions/{ug}/checklist")).Read<JsonElement>();
        ugChecklist.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slot").GetProperty("typeName").GetString()).Should().BeEquivalentTo("Passport", "Academic records", "Nichotan application form");

        var pg = await NewApplicationAsync(counsellor, candidateId, pgProgramme);
        var pgChecklist = await (await counsellor.GetAsync($"/api/admissions/{pg}/checklist")).Read<JsonElement>();
        pgChecklist.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slot").GetProperty("typeName").GetString())
            .Should().BeEquivalentTo("Passport", "Academic records", "Nichotan application form", "CV", "Academic reference", "Professional reference", "Statement of purpose");

        var overview = await OverviewAsync(candidate);
        overview.GetProperty("slots").EnumerateArray().Count(s => s.GetProperty("typeName").GetString() == "Passport").Should().Be(1, "shared documents appear once");
        Slot(overview, "Statement of purpose").GetProperty("applicationId").GetString().Should().Be(pg.ToString(), "a statement belongs to one application");

        var purpose = await TypeIdAsync(admin, "Statement of purpose");
        (await UploadAsync(candidate, purpose, Pdf("sop"), "sop.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "it needs to say which application");
        (await UploadAsync(candidate, purpose, Pdf("sop"), "sop.pdf", applicationId: ug)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not on the undergraduate checklist");
        (await UploadAsync(candidate, purpose, Pdf("sop"), "sop.pdf", applicationId: pg)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(candidate, await TypeIdAsync(admin, "Passport"), Pdf("p"), applicationId: pg)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a shared document isn't tied to an application");

        // The same passport counts on both checklists.
        (await UploadAsync(candidate, await TypeIdAsync(admin, "Passport"), Pdf("p"))).StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var app in new[] { ug, pg })
        {
            var checklist = await (await counsellor.GetAsync($"/api/admissions/{app}/checklist")).Read<JsonElement>();
            checklist.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slot").GetProperty("typeName").GetString() == "Passport").GetProperty("slot").GetProperty("status").GetString().Should().Be("Uploaded");
        }
    }

    [Fact]
    public async Task Template_changes_apply_to_new_applications_only_until_staff_refresh_an_existing_one()
    {
        var (admin, counsellor, _, institutionId, ugProgramme, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);
        var cv = await TypeIdAsync(admin, "CV");
        var existing = await NewApplicationAsync(counsellor, candidateId, ugProgramme);

        (await counsellor.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = cv, institutionId, isRequired = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden, "only administrators edit the template");
        var line = await (await admin.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = cv, institutionId, isRequired = true })).Read<JsonElement>();
        line.GetProperty("institutionName").GetString().Should().StartWith("Docs University");
        (await admin.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = cv, institutionId, isRequired = false })).StatusCode.Should().Be(HttpStatusCode.Conflict, "same document, country, level and institution");

        async Task<string[]> Names(Guid app) => (await (await counsellor.GetAsync($"/api/admissions/{app}/checklist")).Read<JsonElement>())
            .GetProperty("items").EnumerateArray().Where(i => i.GetProperty("slot").GetProperty("required").GetBoolean()).Select(i => i.GetProperty("slot").GetProperty("typeName").GetString()!).ToArray();

        (await Names(existing)).Should().NotContain("CV", "an existing checklist isn't rewritten silently");
        var other = await NewApplicationAsync(counsellor, candidateId, await NewProgrammeAsync(counsellor, institutionId, "Law", "Undergraduate"));
        (await Names(other)).Should().Contain("CV", "a new application takes the current template");

        (await counsellor.PostAsync($"/api/admissions/{existing}/checklist/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Names(existing)).Should().Contain("CV");

        (await counsellor.PutAsJsonAsync($"/api/admissions/{existing}/checklist/items", new { documentTypeId = cv, required = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a waiver needs a reason");
        (await counsellor.PutAsJsonAsync($"/api/admissions/{existing}/checklist/items", new { documentTypeId = cv, required = false, note = "School waived it." })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await counsellor.PostAsync($"/api/admissions/{existing}/checklist/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Names(existing)).Should().NotContain("CV", "a manual waiver survives a refresh");
    }

    [Fact]
    public async Task Moving_an_application_on_with_documents_outstanding_needs_an_explicit_confirmation()
    {
        var (admin, counsellor, reviewer, institutionId, ugProgramme, _) = await SetUpAsync();
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var candidateId = await CandidateIdAsync(candidate);
        var app = await NewApplicationAsync(counsellor, candidateId, ugProgramme);

        var detail = await (await counsellor.GetAsync($"/api/admissions/{app}")).Read<JsonElement>();
        detail.GetProperty("documentGaps").GetProperty("missing").GetInt32().Should().Be(3);

        var refused = await counsellor.PostAsJsonAsync($"/api/admissions/{app}/status", new { toStatus = "ReadyForReview" });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("still missing");

        var forced = await (await counsellor.PostAsJsonAsync($"/api/admissions/{app}/status", new { toStatus = "ReadyForReview", acknowledgeDocumentGaps = true })).Read<JsonElement>();
        forced.GetProperty("history").EnumerateArray().Last().GetProperty("note").GetString().Should().Contain("outstanding");

        // With everything uploaded the move goes through without a confirmation.
        var app2 = await NewApplicationAsync(counsellor, candidateId, await NewProgrammeAsync(counsellor, institutionId, "History", "Undergraduate"));
        foreach (var name in new[] { "Passport", "Academic records" })
        {
            var id = (await (await UploadAsync(candidate, await TypeIdAsync(admin, name), Pdf(name))).Read<JsonElement>()).GetProperty("id").GetGuid();
            await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Accepted" });
        }

        var gaps = (await (await counsellor.GetAsync($"/api/admissions/{app2}")).Read<JsonElement>()).GetProperty("documentGaps");
        gaps.GetProperty("missing").GetInt32().Should().Be(1, "only the unsigned application form is left");
        (await counsellor.PostAsJsonAsync($"/api/admissions/{app2}/status", new { toStatus = "ReadyForReview" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
