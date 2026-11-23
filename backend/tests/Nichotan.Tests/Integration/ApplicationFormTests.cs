using System.Net;
using Nichotan.Application.Common;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ApplicationFormTests(ApiFactory api)
{
    // The built-in signer checks for a PNG header and a plausible size, not that the picture decodes.
    private static readonly string Signature = "data:image/png;base64," + Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. new byte[300]]);

    private static object Data(string level = "Bachelors", bool highSchool = true, bool postSecondary = false) => new
    {
        personal = new { firstName = "Tanaka", middleNames = "T", lastName = "Moyo", gender = "Female", birthDate = "2004-05-17", citizenshipCountry = "Zimbabwe" },
        contact = new
        {
            homeAddress = "1 Test Road", city = "Harare", country = "Zimbabwe", email = "tanaka@example.test", phone = "+263770000000",
            nextOfKinName = "Rudo Moyo", nextOfKinEmail = "rudo@example.test",
        },
        program = new
        {
            destinationCountry = "Canada", programLevel = level, startYear = DateTime.UtcNow.Year + 1, startTerm = "Fall",
            institutions = new[] { "University of Toronto", "McMaster University" }, programChoices = new[] { "Computer Science" },
        },
        academic = new
        {
            highSchools = highSchool ? new[] { new { name = "Harare High", provinceCountry = "Harare, Zimbabwe", started = "2016-01", completed = "2021-11", grade = "TwelveOrEquivalent" } } : [],
            postSecondary = postSecondary ? new[] { new { institution = "University of Zimbabwe", provinceCountry = "Harare, Zimbabwe", started = "2021-08", completed = "2025-06", credentialAwarded = "BSc", credentialAwardedOn = "2025-07" } } : [],
        },
    };

    private static object Sign(string? image = null) => new { signerName = "Tanaka Moyo", signatureImage = image ?? Signature, acceptDeclaration = true, declarationVersion = "1" };

    [Fact]
    public async Task A_new_candidates_form_starts_as_an_unsaved_draft_prefilled_from_their_account()
    {
        var (client, _, email, _) = await Flows.RegisterCandidateAsync(api);

        var form = await (await client.GetAsync("/api/application-form")).Read<JsonElement>();

        form.GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        form.GetProperty("status").GetString().Should().Be("Draft");
        form.GetProperty("data").GetProperty("personal").GetProperty("firstName").GetString().Should().Be("Test");
        form.GetProperty("data").GetProperty("contact").GetProperty("email").GetString().Should().Be(email);
        form.GetProperty("declarationText").GetString().Should().Contain("true and complete");
    }

    [Fact]
    public async Task An_incomplete_draft_can_be_saved_but_not_submitted()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var partial = new { personal = new { firstName = "Tanaka" }, program = new { institutions = new[] { "University of Toronto" } } };

        var saved = await client.PutAsJsonAsync("/api/application-form", partial);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await saved.Read<JsonElement>()).GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);

        var submit = await client.PostAsJsonAsync("/api/application-form/submit", Sign());
        submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await submit.Content.ReadAsStringAsync()).Should().Contain("LastName").And.Contain("DestinationCountry");
    }

    [Fact]
    public async Task Submitting_signs_and_locks_the_form_and_keeps_the_version()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await client.PutAsJsonAsync("/api/application-form", Data())).StatusCode.Should().Be(HttpStatusCode.OK);

        var submitted = await (await client.PostAsJsonAsync("/api/application-form/submit", Sign())).Read<JsonElement>();

        submitted.GetProperty("status").GetString().Should().Be("Submitted");
        submitted.GetProperty("isEditable").GetBoolean().Should().BeFalse();
        submitted.GetProperty("applicantLevel").GetString().Should().Be("Undergraduate");
        submitted.GetProperty("currentVersion").GetInt32().Should().Be(1);

        var version = await (await client.GetAsync("/api/application-form/versions/1")).Read<JsonElement>();
        version.GetProperty("signature").GetProperty("provider").GetString().Should().Be("Native");
        version.GetProperty("signature").GetProperty("signerName").GetString().Should().Be("Tanaka Moyo");
        version.GetProperty("signature").GetProperty("imageDataUrl").GetString().Should().StartWith("data:image/png;base64,");
        version.GetProperty("contentHash").GetString().Should().HaveLength(64);

        (await client.PutAsJsonAsync("/api/application-form", Data())).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a submitted form is locked");
        (await client.PostAsJsonAsync("/api/application-form/submit", Sign())).StatusCode.Should().Be(HttpStatusCode.BadRequest, "and can't be submitted twice");
    }

    [Fact]
    public async Task Postgraduate_applicants_must_list_post_secondary_study_and_undergraduates_a_high_school()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);

        await client.PutAsJsonAsync("/api/application-form", Data("Masters", highSchool: true, postSecondary: false));
        var refused = await client.PostAsJsonAsync("/api/application-form/submit", Sign());
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("PostSecondary");

        await client.PutAsJsonAsync("/api/application-form", Data("Masters", highSchool: false, postSecondary: true));
        var accepted = await (await client.PostAsJsonAsync("/api/application-form/submit", Sign())).Read<JsonElement>();
        accepted.GetProperty("applicantLevel").GetString().Should().Be("Postgraduate");

        var (undergrad, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await undergrad.PutAsJsonAsync("/api/application-form", Data("Diploma", highSchool: false, postSecondary: true));
        var noSchool = await undergrad.PostAsJsonAsync("/api/application-form/submit", Sign());
        noSchool.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noSchool.Content.ReadAsStringAsync()).Should().Contain("HighSchools");
    }

    [Fact]
    public async Task High_school_applicants_are_their_own_level_and_list_the_schools_they_attended()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);

        await client.PutAsJsonAsync("/api/application-form", Data("HighSchool", highSchool: false));
        var refused = await client.PostAsJsonAsync("/api/application-form/submit", Sign());
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("HighSchools");

        await client.PutAsJsonAsync("/api/application-form", Data("HighSchool"));
        var accepted = await (await client.PostAsJsonAsync("/api/application-form/submit", Sign())).Read<JsonElement>();
        accepted.GetProperty("applicantLevel").GetString().Should().Be("HighSchool");
    }

    [Fact]
    public async Task A_signature_that_is_not_a_png_image_is_refused()
    {
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await client.PutAsJsonAsync("/api/application-form", Data());

        var response = await client.PostAsJsonAsync("/api/application-form/submit", Sign("data:image/png;base64," + Convert.ToBase64String(new byte[300])));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await (await client.GetAsync("/api/application-form")).Read<JsonElement>()).GetProperty("status").GetString().Should().Be("Draft");
    }

    [Fact]
    public async Task Staff_can_reopen_a_form_and_every_version_is_kept()
    {
        var admin = await Flows.AdminAsync(api);
        var (client, _, email, _) = await Flows.RegisterCandidateAsync(api);
        await client.PutAsJsonAsync("/api/application-form", Data());
        await client.PostAsJsonAsync("/api/application-form/submit", Sign());
        var candidateId = (await (await client.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        var list = await (await admin.GetAsync("/api/application-forms?status=Submitted&search=" + Uri.EscapeDataString(email))).Read<JsonElement>();
        list.GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("candidateId").GetGuid() == candidateId);

        (await admin.PostAsJsonAsync($"/api/application-forms/{candidateId}/reopen", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var reopened = await (await admin.PostAsJsonAsync($"/api/application-forms/{candidateId}/reopen", new { reason = "Passport spelling doesn't match." })).Read<JsonElement>();
        reopened.GetProperty("status").GetString().Should().Be("Reopened");
        api.Emails.Sent.Should().Contain(m => m.To == email && m.Subject.Contains("needs a few changes") && m.From == EmailSenderKind.Info, "a counsellor-to-student email is sent from the Info address");

        var mine = await (await client.GetAsync("/api/application-form")).Read<JsonElement>();
        mine.GetProperty("reopenReason").GetString().Should().Contain("Passport");
        mine.GetProperty("isEditable").GetBoolean().Should().BeTrue();

        var corrected = Data();
        await client.PutAsJsonAsync("/api/application-form", corrected);
        var second = await (await client.PostAsJsonAsync("/api/application-form/submit", Sign())).Read<JsonElement>();
        second.GetProperty("currentVersion").GetInt32().Should().Be(2);
        second.GetProperty("versions").GetArrayLength().Should().Be(2);

        (await admin.GetAsync($"/api/application-forms/{candidateId}/versions/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync($"/api/application-forms/{candidateId}/versions/3")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_submitted_form_downloads_as_a_pdf_for_the_candidate_and_for_staff_only()
    {
        var admin = await Flows.AdminAsync(api);
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await client.PutAsJsonAsync("/api/application-form", Data("Masters", highSchool: false, postSecondary: true));
        (await client.GetAsync("/api/application-form/versions/1/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound, "nothing is submitted yet");
        await client.PostAsJsonAsync("/api/application-form/submit", Sign());
        var candidateId = (await (await client.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        var mine = await client.GetAsync("/api/application-form/versions/1/pdf");
        mine.StatusCode.Should().Be(HttpStatusCode.OK);
        mine.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");
        var bytes = await mine.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");

        (await admin.GetAsync($"/api/application-forms/{candidateId}/versions/1/pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await other.GetAsync("/api/application-form/versions/1/pdf")).StatusCode.Should().Be(HttpStatusCode.NotFound, "another candidate's version is not theirs");
        (await other.GetAsync($"/api/application-forms/{candidateId}/versions/1/pdf")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Only_a_submitted_form_can_be_reopened()
    {
        var admin = await Flows.AdminAsync(api);
        var (client, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await client.PutAsJsonAsync("/api/application-form", Data());
        var candidateId = (await (await client.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        (await admin.PostAsJsonAsync($"/api/application-forms/{candidateId}/reopen", new { reason = "Please check." })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Candidates_only_see_their_own_form_and_cannot_use_staff_endpoints()
    {
        var (first, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (second, _, _, _) = await Flows.RegisterCandidateAsync(api);
        await first.PutAsJsonAsync("/api/application-form", Data());
        var firstId = (await (await first.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        (await (await second.GetAsync("/api/application-form")).Read<JsonElement>()).GetProperty("id").ValueKind.Should().Be(JsonValueKind.Null);
        (await second.GetAsync($"/api/application-forms/{firstId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await second.GetAsync("/api/application-forms")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await second.PostAsJsonAsync($"/api/application-forms/{firstId}/reopen", new { reason = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync("/api/application-form")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
