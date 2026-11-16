using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

// Visa cases reuse the offer/acceptance set-up above: a visa case hangs off an accepted admission application.
public partial class OutcomeTests
{
    private async Task<Guid> AcceptedAsync(HttpClient counsellor, Guid applicationId)
    {
        (await counsellor.PutAsJsonAsync($"/api/admissions/{applicationId}/outcome/acceptance", new { receivedOn = Today })).StatusCode.Should().Be(HttpStatusCode.OK);
        return applicationId;
    }

    private static async Task<JsonElement> MoveVisaAsync(HttpClient staff, Guid id, string to, object? extra = null, HttpStatusCode expect = HttpStatusCode.OK)
    {
        var body = new Dictionary<string, object?> { ["toStatus"] = to, ["note"] = "ok", ["acknowledgeDocumentGaps"] = true };
        if (extra is not null) foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var response = await staff.PostAsJsonAsync($"/api/visa-cases/{id}/status", body);
        response.StatusCode.Should().Be(expect, await response.Content.ReadAsStringAsync());
        return expect == HttpStatusCode.OK ? await response.Read<JsonElement>() : default;
    }

    [Fact]
    public async Task A_visa_case_needs_an_accepted_application_one_case_each_and_staff_not_students_open_it()
    {
        var (_, counsellor, student, _, applicationId) = await WithOfferAsync();
        (await counsellor.PostAsJsonAsync("/api/visa-cases", new { applicationId })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no formal acceptance yet");

        await AcceptedAsync(counsellor, applicationId);
        (await student.PostAsJsonAsync("/api/visa-cases", new { applicationId })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var opened = await (await counsellor.PostAsJsonAsync("/api/visa-cases", new { applicationId })).Read<JsonElement>();
        opened.GetProperty("summary").GetProperty("status").GetString().Should().Be("Preparing");
        opened.GetProperty("summary").GetProperty("country").GetString().Should().Be("Canada", "taken from the admission, not typed in again");
        (await counsellor.PostAsJsonAsync("/api/visa-cases", new { applicationId })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "one case per application");

        // A student can't see the staff view of a case.
        var id = opened.GetProperty("summary").GetProperty("id").GetGuid();
        (await student.GetAsync($"/api/visa-cases/{id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_visa_case_follows_its_workflow_with_biometrics_and_an_outcome_and_the_student_sees_their_own()
    {
        var (admin, counsellor, student, _, applicationId) = await WithOfferAsync();
        await AcceptedAsync(counsellor, applicationId);
        var opened = await (await counsellor.PostAsJsonAsync("/api/visa-cases", new { applicationId })).Read<JsonElement>();
        var id = opened.GetProperty("summary").GetProperty("id").GetGuid();
        var before = await (await counsellor.GetAsync("/api/visa-cases/stats")).Read<JsonElement>();

        await MoveVisaAsync(counsellor, id, "Approved", expect: HttpStatusCode.BadRequest);
        var submitted = await MoveVisaAsync(counsellor, id, "Submitted", new { referenceNumber = "V404-1" });
        submitted.GetProperty("summary").GetProperty("referenceNumber").GetString().Should().Be("V404-1");
        submitted.GetProperty("summary").GetProperty("submittedOn").ValueKind.Should().Be(JsonValueKind.String);
        (await (await counsellor.GetAsync("/api/visa-cases/stats")).Read<JsonElement>()).GetProperty("inProcess").GetInt32().Should().Be(before.GetProperty("inProcess").GetInt32() + 1);

        var appointment = DateTime.UtcNow.AddDays(10).ToString("yyyy-MM-dd");
        var requested = await MoveVisaAsync(counsellor, id, "BiometricsRequested", new { biometricsAppointmentOn = appointment, biometricsLocation = "VAC Harare" });
        requested.GetProperty("biometricsLocation").GetString().Should().Be("VAC Harare");
        var mine = (await (await student.GetAsync("/api/visa-cases/mine")).Read<JsonElement>()).EnumerateArray().Single();
        mine.GetProperty("status").GetString().Should().Be("BiometricsRequested");
        mine.GetProperty("biometricsLocation").GetString().Should().Be("VAC Harare");

        await MoveVisaAsync(counsellor, id, "BiometricsCompleted");
        await MoveVisaAsync(counsellor, id, "Refused", new { note = "" }, HttpStatusCode.BadRequest);
        var approved = await MoveVisaAsync(counsellor, id, "Approved");
        approved.GetProperty("summary").GetProperty("decisionOn").ValueKind.Should().Be(JsonValueKind.String);
        approved.GetProperty("allowedMoves").GetArrayLength().Should().Be(0);
        await MoveVisaAsync(counsellor, id, "Submitted", expect: HttpStatusCode.BadRequest);
        (await (await counsellor.GetAsync("/api/visa-cases/stats")).Read<JsonElement>()).GetProperty("approved").GetInt32().Should().Be(before.GetProperty("approved").GetInt32() + 1);

        // Only an administrator corrects a status, the earlier entries stay, and the student's timeline leaves the correction out.
        (await counsellor.PostAsJsonAsync($"/api/visa-cases/{id}/correct", new { toStatus = "BiometricsCompleted", note = "Entered by mistake" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var corrected = await (await admin.PostAsJsonAsync($"/api/visa-cases/{id}/correct", new { toStatus = "BiometricsCompleted", note = "Entered by mistake" })).Read<JsonElement>();
        corrected.GetProperty("summary").GetProperty("status").GetString().Should().Be("BiometricsCompleted");
        corrected.GetProperty("history").EnumerateArray().Should().Contain(e => e.GetProperty("isCorrection").GetBoolean());
        corrected.GetProperty("history").GetArrayLength().Should().Be(6);
        var steps = (await (await student.GetAsync("/api/visa-cases/mine")).Read<JsonElement>()).EnumerateArray().Single().GetProperty("history");
        steps.GetArrayLength().Should().Be(5, "the correction is staff-only");
    }

    private static Task<HttpResponseMessage> UploadSharedAsync(HttpClient client, Guid typeId, Guid? candidateId = null)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(typeId.ToString()), "DocumentTypeId" },
            { new ByteArrayContent(Encoding.ASCII.GetBytes("%PDF-1.4\n" + new string(' ', 200))), "File", "doc.pdf" },
        };
        if (candidateId is { } c) form.Add(new StringContent(c.ToString()), "CandidateId");
        return client.PostAsync("/api/documents", form);
    }

    private async Task<(HttpClient Admin, HttpClient Counsellor, HttpClient Student, Guid CandidateId, Guid ApplicationId, Guid VisaId)> WithVisaCaseAsync(string country = "Canada")
    {
        var (admin, counsellor, student, candidateId, applicationId) = await WithOfferAsync(country);
        await AcceptedAsync(counsellor, applicationId);
        var opened = await (await counsellor.PostAsJsonAsync("/api/visa-cases", new { applicationId })).Read<JsonElement>();
        return (admin, counsellor, student, candidateId, applicationId, opened.GetProperty("summary").GetProperty("id").GetGuid());
    }

    private static string[] ChecklistNames(JsonElement checklist) =>
        checklist.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("slot").GetProperty("typeName").GetString()!).ToArray();

    private static async Task<Guid> TypeIdByNameAsync(HttpClient staff, string name) =>
        (await (await staff.GetAsync("/api/document-types")).Read<JsonElement>()).EnumerateArray().Single(t => t.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    [Fact]
    public async Task A_visa_case_gets_the_visa_checklist_for_its_country_kept_apart_from_the_admission_one()
    {
        var (admin, counsellor, student, _, applicationId, visaId) = await WithVisaCaseAsync();
        var canada = ChecklistNames(await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/checklist")).Read<JsonElement>());
        canada.Should().Contain(["Passport", "Proof of funds", "Police clearance certificate", "Visa fee receipt"]);
        canada.Should().NotContain("Blocked account confirmation", "that is a Germany requirement");

        var admission = ChecklistNames(await (await counsellor.GetAsync($"/api/admissions/{applicationId}/checklist")).Read<JsonElement>());
        admission.Should().NotContain("Proof of funds", "the admission checklist doesn't pick up visa lines");

        var germany = await WithVisaCaseAsync("Germany");
        var german = ChecklistNames(await (await germany.Counsellor.GetAsync($"/api/visa-cases/{germany.VisaId}/checklist")).Read<JsonElement>());
        german.Should().Contain(["Blocked account confirmation", "Health insurance proof"]);
        german.Should().NotContain("Police clearance certificate");

        // The student sees the visa documents on their own list, the school-specific one labelled for the visa.
        var mine = await (await student.GetAsync("/api/documents/mine")).Read<JsonElement>();
        var slots = mine.GetProperty("slots").EnumerateArray().ToList();
        slots.Should().Contain(x => x.GetProperty("typeName").GetString() == "Proof of funds");
        slots.Single(x => x.GetProperty("typeName").GetString() == "Study plan").GetProperty("applicationLabel").GetString().Should().StartWith("Visa:");

        // They upload one. Nothing here reviews visa documents (the visa platform does that), so uploading is all that's needed.
        var checklistBefore = await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/checklist")).Read<JsonElement>();
        var missingBefore = checklistBefore.GetProperty("gaps").GetProperty("missing").GetInt32();
        var funds = slots.Single(x => x.GetProperty("typeName").GetString() == "Proof of funds").GetProperty("documentTypeId").GetGuid();
        var uploaded = await (await UploadSharedAsync(student, funds)).Read<JsonElement>();
        (await counsellor.PostAsJsonAsync($"/api/documents/{uploaded.GetProperty("id").GetGuid()}/review", new { decision = "Accepted" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "visa documents aren't reviewed in this system");
        var after = await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/checklist")).Read<JsonElement>();
        var slot = after.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slot").GetProperty("typeName").GetString() == "Proof of funds").GetProperty("slot");
        slot.GetProperty("status").GetString().Should().Be("Uploaded");
        slot.GetProperty("needsReview").GetBoolean().Should().BeFalse();
        after.GetProperty("gaps").GetProperty("missing").GetInt32().Should().Be(missingBefore - 1, "an upload is enough");
        (await (await admin.GetAsync("/api/documents/review-queue?pageSize=100")).Read<JsonElement>()).GetProperty("items").EnumerateArray()
            .Should().NotContain(i => i.GetProperty("typeName").GetString() == "Proof of funds");
    }

    [Fact]
    public async Task Submitting_the_visa_with_documents_outstanding_needs_a_knowing_yes_and_staff_can_waive_with_a_reason()
    {
        var (admin, counsellor, _, _, _, visaId) = await WithVisaCaseAsync();
        var blocked = await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/status", new { toStatus = "Submitted" });
        blocked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blocked.Content.ReadAsStringAsync()).Should().Contain("not yet uploaded");

        // Waiving needs a reason.
        var funds = await TypeIdByNameAsync(admin, "Proof of funds");
        (await counsellor.PutAsJsonAsync($"/api/visa-cases/{visaId}/checklist/items", new { documentTypeId = funds, required = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var waived = await (await counsellor.PutAsJsonAsync($"/api/visa-cases/{visaId}/checklist/items", new { documentTypeId = funds, required = false, note = "Sponsor letter instead" })).Read<JsonElement>();
        waived.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("slot").GetProperty("typeName").GetString() == "Proof of funds").GetProperty("slot").GetProperty("required").GetBoolean().Should().BeFalse();

        var go = await (await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/status", new { toStatus = "Submitted", acknowledgeDocumentGaps = true })).Read<JsonElement>();
        go.GetProperty("summary").GetProperty("status").GetString().Should().Be("Submitted");
        go.GetProperty("history").EnumerateArray().Last().GetProperty("note").GetString().Should().Contain("outstanding");
    }

    [Fact]
    public async Task The_biometrics_letter_comes_from_the_student_after_submission_and_the_decision_letter_from_staff()
    {
        var (admin, counsellor, student, candidateId, applicationId, visaId) = await WithVisaCaseAsync();
        var biometrics = await PurposeIdAsync(admin, "BiometricsLetter");
        var decision = await PurposeIdAsync(admin, "DecisionLetter");

        (await UploadAsync(student, biometrics, applicationId)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "nothing has been submitted yet");
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        (await UploadAsync(student, biometrics, applicationId, name: "bio.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        var mine = (await (await student.GetAsync("/api/visa-cases/mine")).Read<JsonElement>()).EnumerateArray().Single();
        mine.GetProperty("biometricsLetter").GetProperty("fileName").GetString().Should().Be("bio.pdf");
        (await (await counsellor.GetAsync($"/api/visa-cases/{visaId}")).Read<JsonElement>()).GetProperty("biometricsLetter").ValueKind.Should().Be(JsonValueKind.Object);

        // The decision is staff's, and only once it has been recorded.
        (await UploadAsync(student, decision, applicationId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await UploadAsync(counsellor, decision, applicationId, candidateId)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no decision recorded yet");
        await MoveVisaAsync(counsellor, visaId, "Approved");
        (await UploadAsync(counsellor, decision, applicationId, candidateId, "decision.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await student.GetAsync("/api/visa-cases/mine")).Read<JsonElement>()).EnumerateArray().Single().GetProperty("decisionLetter").GetProperty("fileName").GetString().Should().Be("decision.pdf");

        // These letters are not checklist documents: reviewers never see them in the queue.
        (await (await admin.GetAsync("/api/documents/review-queue?pageSize=100")).Read<JsonElement>()).GetProperty("items").EnumerateArray()
            .Should().NotContain(i => i.GetProperty("typeName").GetString() == "Biometrics letter" || i.GetProperty("typeName").GetString() == "Visa decision letter");
    }

    [Fact]
    public async Task The_visa_template_is_edited_by_administrators_and_a_line_cannot_change_stage()
    {
        var (admin, counsellor, _, _, _, _) = await WithVisaCaseAsync();
        var funds = await TypeIdByNameAsync(admin, "Proof of funds");
        (await counsellor.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = funds, isRequired = true, stage = "Visa", country = "Germany" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var created = await (await admin.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = funds, isRequired = true, stage = "Visa", country = "Germany" })).Read<JsonElement>();
        created.GetProperty("stage").GetString().Should().Be("Visa");
        var id = created.GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/document-requirements/{id}", new { documentTypeId = funds, isRequired = true, stage = "Admission", country = "Germany" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync("/api/document-requirements", new { documentTypeId = funds, isRequired = false, stage = "Visa", country = "Germany" })).StatusCode.Should().Be(HttpStatusCode.Conflict, "same stage, country and level");
    }

    [Fact]
    public async Task Submitting_starts_the_biometrics_reminder_and_the_student_or_staff_can_say_they_are_done()
    {
        var (_, counsellor, student, _, _, visaId) = await WithVisaCaseAsync();
        async Task<JsonElement?> FollowUpAsync() =>
            (await (await counsellor.GetAsync("/api/visa-cases/biometrics-follow-ups")).Read<JsonElement>()).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(i => i!.Value.GetProperty("visaCaseId").GetGuid() == visaId);

        // Nothing to remind about until the application has been submitted on the visa platform.
        (await FollowUpAsync()).Should().BeNull();
        (await student.PostAsJsonAsync($"/api/visa-cases/mine/{visaId}/biometrics-done", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "not submitted yet");

        await MoveVisaAsync(counsellor, visaId, "Submitted");
        api.Emails.Sent.Should().Contain(m => m.Subject == "Your visa application has been submitted" && m.TextBody.Contains("/portal/applications"));
        var item = await FollowUpAsync();
        item.Should().NotBeNull("the biometrics reminder starts when the visa is submitted");
        item!.Value.GetProperty("overdue").GetBoolean().Should().BeFalse();
        (await (await student.GetAsync("/api/visa-cases/mine")).Read<JsonElement>()).EnumerateArray().Single().GetProperty("canMarkBiometricsDone").GetBoolean().Should().BeTrue();

        // An appointment date that has gone by with nothing marked done makes it overdue.
        await counsellor.PutAsJsonAsync($"/api/visa-cases/{visaId}/details", new { biometricsAppointmentOn = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd") });
        (await FollowUpAsync())!.Value.GetProperty("overdue").GetBoolean().Should().BeTrue();

        // Another student can't mark it; this student can, and staff are told.
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await other.PostAsJsonAsync($"/api/visa-cases/mine/{visaId}/biometrics-done", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student.PostAsJsonAsync($"/api/visa-cases/mine/{visaId}/biometrics-done", new { doneOn = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") })).StatusCode.Should().Be(HttpStatusCode.OK);
        api.Emails.Sent.Should().Contain(m => m.To == "enquiries@nichotan.com" && m.Subject == "A student has done their biometrics" && m.TextBody.Contains(visaId.ToString()));
        var detail = await (await counsellor.GetAsync($"/api/visa-cases/{visaId}")).Read<JsonElement>();
        detail.GetProperty("summary").GetProperty("status").GetString().Should().Be("BiometricsCompleted");
        detail.GetProperty("history").EnumerateArray().Last().GetProperty("note").GetString().Should().Contain("student");
        (await FollowUpAsync()).Should().BeNull("the reminder stops once biometrics are done");
        (await student.PostAsJsonAsync($"/api/visa-cases/mine/{visaId}/biometrics-done", new { })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already done");

        // Staff can flag it themselves too, straight after submitting.
        var second = await WithVisaCaseAsync();
        await MoveVisaAsync(second.Counsellor, second.VisaId, "Submitted");
        (await MoveVisaAsync(second.Counsellor, second.VisaId, "BiometricsCompleted")).GetProperty("summary").GetProperty("status").GetString().Should().Be("BiometricsCompleted");
    }
}
