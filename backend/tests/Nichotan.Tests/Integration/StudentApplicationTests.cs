using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class StudentApplicationTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;
    private static readonly string Signature = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[300]]);

    private static object FormData(string level = "Bachelors") => new
    {
        personal = new { firstName = "Sam", lastName = "Student", gender = "Male", birthDate = "2004-05-17", citizenshipCountry = "Zimbabwe" },
        contact = new { homeAddress = "1 Test Road", city = "Harare", country = "Zimbabwe", email = "s@example.test", phone = "+263770000000", nextOfKinName = "Rudo", nextOfKinEmail = "r@example.test" },
        program = new { destinationCountry = "Canada", programLevel = level, startYear = Year, startTerm = "Fall", institutions = new[] { "UofT" }, programChoices = new[] { "CS" } },
        academic = new { highSchools = new[] { new { name = "Harare High", provinceCountry = "Harare, Zimbabwe", started = "2016-01", completed = "2021-11", grade = "TwelveOrEquivalent" } }, postSecondary = Array.Empty<object>() },
    };

    private static async Task SignFormAsync(HttpClient candidate, string level = "Bachelors")
    {
        (await candidate.PutAsJsonAsync("/api/application-form", FormData(level))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await candidate.PostAsJsonAsync("/api/application-form/submit", new { signerName = "Sam Student", signatureImage = Signature, acceptDeclaration = true, declarationVersion = "1" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task UploadAsync(HttpClient candidate, HttpClient staff, string typeName)
    {
        var types = await (await staff.GetAsync("/api/document-types")).Read<JsonElement>();
        var typeId = types.EnumerateArray().Single(t => t.GetProperty("name").GetString() == typeName).GetProperty("id").GetGuid();
        var form = new MultipartFormDataContent { { new StringContent(typeId.ToString()), "DocumentTypeId" }, { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string(' ', 200))), "File", "doc.pdf" } };
        (await candidate.PostAsync("/api/documents", form)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<(HttpClient Admin, HttpClient Counsellor, Guid InstitutionId, List<Guid> Programmes)> SetUpAsync(int programmes = 2, string level = "Undergraduate")
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "Student University " + Guid.NewGuid().ToString("N")[..8], country = "Canada", notes = "PRIVATE STAFF NOTE" })).Read<JsonElement>();
        var institutionId = institution.GetProperty("id").GetGuid();
        var ids = new List<Guid>();
        for (var i = 0; i < programmes; i++)
        {
            var p = await (await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId, name = $"Programme {i} {Guid.NewGuid().ToString("N")[..4]}", level, intakes = new[] { new { term = "Fall", year = Year }, new { term = "Winter", year = 2001 } } })).Read<JsonElement>();
            ids.Add(p.GetProperty("id").GetGuid());
        }

        return (admin, counsellor, institutionId, ids);
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient candidate, Guid programmeId, string term = "Fall", int? year = null) =>
        candidate.PostAsJsonAsync("/api/admissions/mine", new { programmeId, intakeTerm = term, intakeYear = year ?? Year });

    [Fact]
    public async Task Students_choose_from_the_directory_without_seeing_staff_notes_or_past_intakes()
    {
        var (_, counsellor, institutionId, programmes) = await SetUpAsync(1);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var archived = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "Archived University " + Guid.NewGuid().ToString("N")[..8], country = "Germany" })).Read<JsonElement>();
        await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId = archived.GetProperty("id").GetGuid(), name = "Hidden", level = "Undergraduate", intakes = new[] { new { term = "Fall", year = Year } } });
        await counsellor.PostAsJsonAsync($"/api/institutions/{archived.GetProperty("id").GetGuid()}/archive", new { archived = true });

        var directory = await (await candidate.GetAsync("/api/admissions/directory")).Read<JsonElement>();
        var mine = directory.EnumerateArray().Single(i => i.GetProperty("id").GetGuid() == institutionId);
        mine.GetProperty("programmes").EnumerateArray().Single().GetProperty("intakes").GetArrayLength().Should().Be(1, "the 2001 intake is in the past");
        directory.GetRawText().Should().NotContain("PRIVATE STAFF NOTE").And.NotContain("Archived University");
        (await api.NewClient().GetAsync("/api/admissions/directory")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await counsellor.GetAsync("/api/admissions/directory")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_student_creates_their_own_application_with_limits_and_a_level_check()
    {
        var (_, counsellor, _, programmes) = await SetUpAsync(6);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);

        var created = await (await CreateAsync(candidate, programmes[0])).Read<JsonElement>();
        created.GetProperty("status").GetString().Should().Be("Draft");
        created.GetProperty("canSend").GetBoolean().Should().BeFalse();
        created.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).Should().Contain(["Sign and submit your application form", "Upload Passport", "Upload Academic records"]);

        (await CreateAsync(candidate, programmes[0])).StatusCode.Should().Be(HttpStatusCode.Conflict, "the same programme and intake");
        (await CreateAsync(candidate, programmes[1], "Summer")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not an intake the programme lists");
        for (var i = 1; i < 5; i++) (await CreateAsync(candidate, programmes[i])).StatusCode.Should().Be(HttpStatusCode.OK);
        var sixth = await CreateAsync(candidate, programmes[5]);
        sixth.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await sixth.Content.ReadAsStringAsync()).Should().Contain("up to 5");

        // Staff see it straight away, created by the student.
        var staffView = await (await counsellor.GetAsync("/api/admissions?search=Student University")).Read<JsonElement>();
        staffView.GetProperty("total").GetInt32().Should().BeGreaterThan(0);

        // A student whose form says Postgraduate can't apply to an undergraduate programme.
        var (grad, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await grad.PutAsJsonAsync("/api/application-form", FormData("Masters"));
        var mismatch = await CreateAsync(grad, programmes[0]);
        mismatch.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatch.Content.ReadAsStringAsync()).Should().Contain("different level");
    }

    [Fact]
    public async Task A_student_can_send_only_when_the_form_is_signed_and_every_required_document_is_uploaded()
    {
        var (admin, counsellor, _, programmes) = await SetUpAsync(1);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var id = (await (await CreateAsync(candidate, programmes[0])).Read<JsonElement>()).GetProperty("id").GetGuid();

        var early = await candidate.PostAsync($"/api/admissions/mine/{id}/send", null);
        early.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await early.Content.ReadAsStringAsync()).Should().Contain("application form");

        await SignFormAsync(candidate);
        await UploadAsync(candidate, admin, "Passport");
        (await candidate.PostAsync($"/api/admissions/mine/{id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "academic records are still missing");

        await UploadAsync(candidate, admin, "Academic records");
        var ready = (await (await candidate.GetAsync("/api/admissions/mine")).Read<JsonElement>()).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == id);
        ready.GetProperty("canSend").GetBoolean().Should().BeTrue("uploaded is enough: accepting is the counsellor's job");

        var before = (await (await counsellor.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("toReview").GetInt32();
        var sent = await (await candidate.PostAsync($"/api/admissions/mine/{id}/send", null)).Read<JsonElement>();
        sent.GetProperty("status").GetString().Should().Be("ReadyForReview");
        sent.GetProperty("canSend").GetBoolean().Should().BeFalse();
        (await (await counsellor.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("toReview").GetInt32().Should().Be(before + 1);
        (await candidate.PostAsync($"/api/admissions/mine/{id}/send", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already sent");

        var history = (await (await counsellor.GetAsync($"/api/admissions/{id}")).Read<JsonElement>()).GetProperty("history").EnumerateArray().Last();
        history.GetProperty("note").GetString().Should().Contain("by the student");

        // The team is emailed a link, and the student isn't emailed back.
        var mail = api.Emails.Sent.Single(m => m.To == "enquiries@nichotan.com" && m.Subject == "An application is ready for review" && m.TextBody.Contains(id.ToString()));
        mail.TextBody.Should().Contain($"https://web.test/staff/admissions/{id}").And.Contain("Test Candidate").And.Contain("Student University");
        mail.TextBody.Should().NotContain("@example.test", "no contact details").And.NotContain("+263", "no phone number");
        api.Emails.Sent.Should().NotContain(m => m.Subject == "An application is ready for review" && m.To != "enquiries@nichotan.com");
    }

    [Fact]
    public async Task A_student_can_withdraw_until_staff_have_submitted_it_to_the_school_and_only_their_own()
    {
        var (admin, counsellor, _, programmes) = await SetUpAsync(2);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var first = (await (await CreateAsync(candidate, programmes[0])).Read<JsonElement>()).GetProperty("id").GetGuid();
        var second = (await (await CreateAsync(candidate, programmes[1])).Read<JsonElement>()).GetProperty("id").GetGuid();

        (await other.PostAsync($"/api/admissions/mine/{first}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/admissions/mine/{first}/send", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await counsellor.PostAsync($"/api/admissions/mine/{first}/withdraw", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var withdrawn = await (await candidate.PostAsync($"/api/admissions/mine/{first}/withdraw", null)).Read<JsonElement>();
        withdrawn.GetProperty("status").GetString().Should().Be("Withdrawn");
        withdrawn.GetProperty("canWithdraw").GetBoolean().Should().BeFalse();

        await SignFormAsync(candidate);
        await UploadAsync(candidate, admin, "Passport");
        await UploadAsync(candidate, admin, "Academic records");
        await candidate.PostAsync($"/api/admissions/mine/{second}/send", null);
        await counsellor.PostAsJsonAsync($"/api/admissions/{second}/status", new { toStatus = "SubmittedToInstitution", acknowledgeDocumentGaps = true });
        var tooLate = await candidate.PostAsync($"/api/admissions/mine/{second}/withdraw", null);
        tooLate.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooLate.Content.ReadAsStringAsync()).Should().Contain("counsellor");
    }

    [Fact]
    public async Task A_student_can_suggest_a_school_that_is_not_listed_and_staff_deal_with_it()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var school = "Suggested College " + Guid.NewGuid().ToString("N")[..8];

        (await candidate.PostAsJsonAsync("/api/admissions/suggestions", new { institutionName = "", country = "Canada", programmeName = "x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var suggestion = await (await candidate.PostAsJsonAsync("/api/admissions/suggestions", new { institutionName = school, country = "Canada", programmeName = "Nursing", intakeTerm = "Fall", intakeYear = Year, note = "My cousin studied there." })).Read<JsonElement>();
        suggestion.GetProperty("status").GetString().Should().Be("New");

        var queue = await (await counsellor.GetAsync("/api/admissions/suggestions?status=New")).Read<JsonElement>();
        queue.EnumerateArray().Should().Contain(s => s.GetProperty("institutionName").GetString() == school && s.GetProperty("candidateName").GetString() == "Test Candidate");
        var id = suggestion.GetProperty("id").GetGuid();
        (await candidate.GetAsync("/api/admissions/suggestions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await counsellor.PutAsJsonAsync($"/api/admissions/suggestions/{id}", new { status = "Added", staffNote = "Internal: added to the directory." });
        var mine = await (await candidate.GetAsync("/api/admissions/suggestions/mine")).Read<JsonElement>();
        mine.GetRawText().Should().Contain("Added").And.NotContain("Internal: added", "staff notes are private");
    }
}
