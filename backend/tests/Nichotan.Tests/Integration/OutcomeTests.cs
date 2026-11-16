using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public partial class OutcomeTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;
    private static string Today => DateTime.UtcNow.ToString("yyyy-MM-dd");

    private async Task<Guid> PurposeIdAsync(HttpClient staff, string purpose) =>
        (await (await staff.GetAsync("/api/document-types")).Read<JsonElement>()).EnumerateArray().Single(t => t.GetProperty("purpose").GetString() == purpose).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid typeId, Guid applicationId, Guid? candidateId = null, string name = "letter.pdf")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "DocumentTypeId" },
            { new StringContent(applicationId.ToString()), "ApplicationId" },
            { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string(' ', 200))), "File", name },
        };
        if (candidateId is { } c) form.Add(new StringContent(c.ToString()), "CandidateId");
        return client.PostAsync("/api/documents", form);
    }

    /// <summary>A student with an application that has an offer recorded, ready for the acceptance step.</summary>
    private async Task<(HttpClient Admin, HttpClient Counsellor, HttpClient Student, Guid CandidateId, Guid ApplicationId)> WithOfferAsync(string country = "Canada", string level = "Undergraduate", bool recordOffer = true)
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = $"Outcome {country} " + Guid.NewGuid().ToString("N")[..8], country })).Read<JsonElement>();
        var programme = await (await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId = institution.GetProperty("id").GetGuid(), name = "Study", level, intakes = new[] { new { term = "Fall", year = Year } } })).Read<JsonElement>();
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var created = await (await student.PostAsJsonAsync("/api/admissions/mine", new { programmeId = programme.GetProperty("id").GetGuid(), intakeTerm = "Fall", intakeYear = Year })).Read<JsonElement>();
        var applicationId = created.GetProperty("id").GetGuid();
        var candidateId = (await (await student.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        foreach (var to in new[] { "ReadyForReview", "SubmittedToInstitution", "OfferReceived" })
        {
            var move = await counsellor.PostAsJsonAsync($"/api/admissions/{applicationId}/status", new { toStatus = to, acknowledgeDocumentGaps = true, note = "ok" });
            // The school can't be applied to before the student's form is signed, so the test steps past that one move.
            if (to == "SubmittedToInstitution" && move.StatusCode == HttpStatusCode.BadRequest) await SignAsync(student, counsellor, applicationId);
        }

        if (recordOffer) (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/offer", new { conditions = "Maths 70%", respondByOn = (string?)null })).StatusCode.Should().Be(HttpStatusCode.OK);
        return (admin, counsellor, student, candidateId, applicationId);
    }

    private static async Task SignAsync(HttpClient student, HttpClient counsellor, Guid applicationId)
    {
        var form = new
        {
            personal = new { firstName = "T", lastName = "C", gender = "Male", birthDate = "2004-05-17", citizenshipCountry = "Zimbabwe" },
            contact = new { homeAddress = "1 Rd", city = "Harare", country = "Zimbabwe", email = "t@example.test", phone = "+263770000000", nextOfKinName = "R", nextOfKinEmail = "r@example.test" },
            program = new { destinationCountry = "Canada", programLevel = "Bachelors", startYear = Year, startTerm = "Fall", institutions = new[] { "x" }, programChoices = new[] { "y" } },
            academic = new { highSchools = new[] { new { name = "H", provinceCountry = "Harare", started = "2016-01", completed = "2021-11", grade = "TwelveOrEquivalent" } }, postSecondary = Array.Empty<object>() },
        };
        await student.PutAsJsonAsync("/api/application-form", form);
        await student.PostAsJsonAsync("/api/application-form/submit", new { signerName = "T C", signatureImage = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[300]]), acceptDeclaration = true, declarationVersion = "1" });
        var status = await counsellor.PostAsJsonAsync($"/api/admissions/{applicationId}/status", new { toStatus = "SubmittedToInstitution", acknowledgeDocumentGaps = true });
        status.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> MineAsync(HttpClient student, Guid applicationId) =>
        (await (await student.GetAsync("/api/admissions/mine")).Read<JsonElement>()).EnumerateArray().Single(a => a.GetProperty("id").GetGuid() == applicationId);

    [Fact]
    public async Task An_offer_can_only_be_recorded_once_the_application_has_one_and_the_student_is_told_and_can_read_it()
    {
        var (admin, counsellor, student, candidateId, applicationId) = await WithOfferAsync(recordOffer: false);

        var outcome = await (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/offer", new { conditions = "Maths 70%", respondByOn = DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-dd") })).Read<JsonElement>();
        outcome.GetProperty("offer").GetProperty("conditions").GetString().Should().Be("Maths 70%");
        api.Emails.Sent.Should().Contain(m => m.Subject == "You have an offer to look at" && m.TextBody.Contains("/portal/applications"));

        var letterType = await PurposeIdAsync(admin, "OfferLetter");
        // The student uploads the letter the school sent them, and staff are told so they can record the offer.
        (await UploadAsync(student, letterType, applicationId, name: "my-offer.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        api.Emails.Sent.Should().Contain(m => m.To == "enquiries@nichotan.com" && m.Subject == "A student has uploaded an offer letter" && m.TextBody.Contains(applicationId.ToString()));
        (await UploadAsync(counsellor, letterType, applicationId, candidateId, "offer.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await MineAsync(student, applicationId);
        mine.GetProperty("offer").GetProperty("conditions").GetString().Should().Be("Maths 70%");
        var documentId = mine.GetProperty("offer").GetProperty("letterDocumentId").GetGuid();
        (await student.GetAsync($"/api/documents/{documentId}/file")).StatusCode.Should().Be(HttpStatusCode.OK, "the student can read their own offer letter");
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await other.GetAsync($"/api/documents/{documentId}/file")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_student_can_upload_the_offer_letter_before_staff_record_the_offer_but_not_before_the_school_has_been_applied_to()
    {
        // At Draft the school hasn't been applied to, so there can't be an offer letter yet.
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "Early Letter " + Guid.NewGuid().ToString("N")[..8], country = "Canada" })).Read<JsonElement>();
        var programme = await (await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId = institution.GetProperty("id").GetGuid(), name = "Study", level = "Undergraduate", intakes = new[] { new { term = "Fall", year = Year } } })).Read<JsonElement>();
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var created = await (await student.PostAsJsonAsync("/api/admissions/mine", new { programmeId = programme.GetProperty("id").GetGuid(), intakeTerm = "Fall", intakeYear = Year })).Read<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        var letterType = await PurposeIdAsync(admin, "OfferLetter");

        created.GetProperty("canUploadOfferLetter").GetBoolean().Should().BeFalse();
        (await UploadAsync(student, letterType, id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Once staff have applied for the student, the school may write back to them directly.
        await counsellor.PostAsJsonAsync($"/api/admissions/{id}/status", new { toStatus = "ReadyForReview", acknowledgeDocumentGaps = true });
        await SignAsync(student, counsellor, id);
        (await MineAsync(student, id)).GetProperty("canUploadOfferLetter").GetBoolean().Should().BeTrue();
        (await UploadAsync(student, letterType, id)).StatusCode.Should().Be(HttpStatusCode.OK, "no offer has been recorded yet, and that's fine");
        var mine = await MineAsync(student, id);
        mine.GetProperty("offerLetter").GetProperty("fileName").GetString().Should().Be("letter.pdf");
        mine.GetProperty("offer").ValueKind.Should().Be(JsonValueKind.Null, "staff haven't recorded the offer yet");
        var outcome = await (await counsellor.GetAsync($"/api/admissions/{id}/outcome")).Read<JsonElement>();
        outcome.GetProperty("offerLetter").ValueKind.Should().Be(JsonValueKind.Object, "staff can see what the student uploaded");

        // The other school letters stay with staff.
        (await UploadAsync(student, await PurposeIdAsync(admin, "AcceptanceLetter"), id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UploadAsync(student, await PurposeIdAsync(admin, "PalLetter"), id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_formal_acceptance_is_its_own_record_and_a_PAL_number_only_goes_with_a_Canadian_undergraduate()
    {
        var (admin, counsellor, student, candidateId, applicationId) = await WithOfferAsync();
        var before = (await (await counsellor.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("acceptances").GetInt32();

        (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/acceptance", new { receivedOn = DateTime.UtcNow.AddDays(3).ToString("yyyy-MM-dd") })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not in the future");
        var palType = await PurposeIdAsync(admin, "PalLetter");
        var letterType = await PurposeIdAsync(admin, "AcceptanceLetter");
        (await UploadAsync(counsellor, letterType, applicationId, candidateId)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "record the acceptance first");

        var recorded = await (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/acceptance", new { receivedOn = Today, palNumber = "pal-12345", note = "Arrived by email" })).Read<JsonElement>();
        recorded.GetProperty("palApplies").GetBoolean().Should().BeTrue();
        recorded.GetProperty("acceptance").GetProperty("palNumber").GetString().Should().Be("PAL-12345");
        (await UploadAsync(counsellor, letterType, applicationId, candidateId, "acceptance.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(counsellor, palType, applicationId, candidateId, "pal.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(student, letterType, applicationId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var mine = (await MineAsync(student, applicationId)).GetProperty("acceptance");
        mine.GetProperty("palNumber").GetString().Should().Be("PAL-12345");
        mine.GetProperty("letterDocumentId").ValueKind.Should().Be(JsonValueKind.String);
        mine.GetProperty("palLetterDocumentId").ValueKind.Should().Be(JsonValueKind.String);
        (await (await counsellor.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("acceptances").GetInt32().Should().Be(before + 1);

        // A PAL is optional: an acceptance without one is fine. And it doesn't apply to Germany or to postgraduates.
        var canadaNoPal = await WithOfferAsync();
        (await canadaNoPal.Counsellor.PutAsJsonAsync($"/api/admissions/{canadaNoPal.ApplicationId}/outcome/acceptance", new { receivedOn = Today })).StatusCode.Should().Be(HttpStatusCode.OK);
        var germany = await WithOfferAsync("Germany");
        (await germany.Counsellor.PutAsJsonAsync($"/api/admissions/{germany.ApplicationId}/outcome/acceptance", new { receivedOn = Today, palNumber = "PAL-1" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var germanyAccepted = await (await germany.Counsellor.PutAsJsonAsync($"/api/admissions/{germany.ApplicationId}/outcome/acceptance", new { receivedOn = Today })).Read<JsonElement>();
        germanyAccepted.GetProperty("palApplies").GetBoolean().Should().BeFalse();
        (await UploadAsync(germany.Counsellor, palType, germany.ApplicationId, germany.CandidateId)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var postgraduate = await WithOfferAsync(level: "Postgraduate");
        (await postgraduate.Counsellor.PutAsJsonAsync($"/api/admissions/{postgraduate.ApplicationId}/outcome/acceptance", new { receivedOn = Today, palNumber = "PAL-1" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Staff_are_reminded_to_chase_the_PAL_once_the_acceptance_letter_is_in_until_it_arrives()
    {
        var (admin, counsellor, _, candidateId, applicationId) = await WithOfferAsync();
        async Task<JsonElement?> FollowUpAsync() =>
            (await (await counsellor.GetAsync("/api/admissions/pal-follow-ups")).Read<JsonElement>()).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(i => i!.Value.GetProperty("applicationId").GetGuid() == applicationId);

        (await FollowUpAsync()).Should().BeNull("the school hasn't accepted yet, so there's no PAL to wait for");
        await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/acceptance", new { receivedOn = Today });
        var item = await FollowUpAsync();
        item.Should().NotBeNull("the acceptance is in but no PAL");
        item!.Value.GetProperty("overdue").GetBoolean().Should().BeFalse("it only just arrived");
        (await (await counsellor.GetAsync("/api/admissions/stats")).Read<JsonElement>()).GetProperty("palFollowUps").GetInt32().Should().BeGreaterThan(0);

        // An acceptance that arrived long ago with no PAL is overdue.
        var old = await WithOfferAsync();
        await old.Counsellor.PutAsJsonAsync($"/api/admissions/{old.ApplicationId}/outcome/acceptance", new { receivedOn = DateTime.UtcNow.AddDays(-5).ToString("yyyy-MM-dd") });
        var list = await (await old.Counsellor.GetAsync("/api/admissions/pal-follow-ups")).Read<JsonElement>();
        list.EnumerateArray().Single(i => i.GetProperty("applicationId").GetGuid() == old.ApplicationId).GetProperty("overdue").GetBoolean().Should().BeTrue();

        (await UploadAsync(counsellor, await PurposeIdAsync(admin, "PalLetter"), applicationId, candidateId, "pal.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await FollowUpAsync()).Should().BeNull("the PAL has arrived");

        // A PAL number alone also settles it.
        await old.Counsellor.PutAsJsonAsync($"/api/admissions/{old.ApplicationId}/outcome/acceptance", new { receivedOn = Today, palNumber = "PAL-1" });
        (await (await old.Counsellor.GetAsync("/api/admissions/pal-follow-ups")).Read<JsonElement>()).EnumerateArray().Should().NotContain(i => i.GetProperty("applicationId").GetGuid() == old.ApplicationId);
    }

    [Fact]
    public async Task Only_counsellors_and_admins_record_outcomes_and_students_cannot()
    {
        var (_, counsellor, student, _, applicationId) = await WithOfferAsync();
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);

        foreach (var client in new[] { student, other })
        {
            (await client.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/offer", new { conditions = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/acceptance", new { receivedOn = Today })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await student.GetAsync($"/api/admissions/{applicationId}/outcome")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync($"/api/admissions/{applicationId}/outcome")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/offer", new { conditions = "Updated" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
