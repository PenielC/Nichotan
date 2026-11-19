using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

/// <summary>A document type that allows several files (academic records) holds a set, reviewed as one.</summary>
[Collection(ApiCollection.Name)]
public class MultiFileTests(ApiFactory api)
{
    private static byte[] Pdf(string text) => Encoding.ASCII.GetBytes("%PDF-1.4\n" + text + new string(' ', 200));

    private static async Task<Guid> TypeIdAsync(HttpClient staff, string name) =>
        (await (await staff.GetAsync("/api/document-types")).Read<JsonElement>()).EnumerateArray().Single(t => t.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid typeId, string text, string fileName, int? position = null)
    {
        var form = new MultipartFormDataContent { { new StringContent(typeId.ToString()), "DocumentTypeId" } };
        if (position is { } p) form.Add(new StringContent(p.ToString()), "Position");
        form.Add(new ByteArrayContent(Pdf(text)), "File", fileName);
        return client.PostAsync("/api/documents", form);
    }

    private static async Task<JsonElement> RecordsSlotAsync(HttpClient candidate) =>
        (await (await candidate.GetAsync("/api/documents/mine")).Read<JsonElement>()).GetProperty("slots").EnumerateArray().Single(s => s.GetProperty("typeName").GetString() == "Academic records");

    private static string[] FileNames(JsonElement slot) => slot.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("fileName").GetString()!).ToArray();

    [Fact]
    public async Task Academic_records_hold_several_files_that_are_all_kept_and_reviewed_together()
    {
        var admin = await Flows.AdminAsync(api);
        var (reviewer, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var records = await TypeIdAsync(admin, "Academic records");

        (await RecordsSlotAsync(candidate)).GetProperty("allowsMultipleFiles").GetBoolean().Should().BeTrue();
        var first = await (await UploadAsync(candidate, records, "grade 11", "grade-11.pdf")).Read<JsonElement>();
        await UploadAsync(candidate, records, "grade 12", "grade-12.pdf");
        await UploadAsync(candidate, records, "certificate", "certificate.pdf");

        var slot = await RecordsSlotAsync(candidate);
        FileNames(slot).Should().Equal("grade-11.pdf", "grade-12.pdf", "certificate.pdf");
        slot.GetProperty("status").GetString().Should().Be("Uploaded");
        var id = first.GetProperty("id").GetGuid();

        // Every file can be downloaded on its own, by the version it was uploaded as.
        var twelve = slot.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("fileName").GetString() == "grade-12.pdf");
        (await (await candidate.GetAsync($"/api/documents/{id}/file?version={twelve.GetProperty("versionNumber").GetInt32()}")).Content.ReadAsByteArrayAsync()).Should().Equal(Pdf("grade 12"));

        // The reviewer sees one item for the set and makes one decision for it.
        var queue = await (await reviewer.GetAsync("/api/documents/review-queue?pageSize=100")).Read<JsonElement>();
        var item = queue.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("documentId").GetGuid() == id);
        item.GetProperty("fileName").GetString().Should().Be("3 files");
        item.GetProperty("fileCount").GetInt32().Should().Be(3);
        (await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Accepted" })).StatusCode.Should().Be(HttpStatusCode.OK);

        // Once accepted the student can't add to it or remove from it.
        (await UploadAsync(candidate, records, "late", "late.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await candidate.DeleteAsync($"/api/documents/{id}/files/1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_file_can_be_replaced_or_removed_and_the_earlier_one_stays_on_record()
    {
        var admin = await Flows.AdminAsync(api);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var records = await TypeIdAsync(admin, "Academic records");
        var id = (await (await UploadAsync(candidate, records, "one", "one.pdf")).Read<JsonElement>()).GetProperty("id").GetGuid();
        await UploadAsync(candidate, records, "two", "two.pdf");

        // Replace file 2: it is still two files, and the old version is still there.
        var replaced = await (await UploadAsync(candidate, records, "two again", "two-fixed.pdf", position: 2)).Read<JsonElement>();
        FileNames(await RecordsSlotAsync(candidate)).Should().Equal("one.pdf", "two-fixed.pdf");
        replaced.GetProperty("versions").GetArrayLength().Should().Be(3);

        // Remove file 1: it stops counting but is kept.
        var removed = await (await candidate.DeleteAsync($"/api/documents/{id}/files/1")).Read<JsonElement>();
        FileNames(await RecordsSlotAsync(candidate)).Should().Equal("two-fixed.pdf");
        removed.GetProperty("versions").EnumerateArray().Should().Contain(v => v.GetProperty("fileName").GetString() == "one.pdf" && v.GetProperty("removed").GetBoolean());
        (await candidate.DeleteAsync($"/api/documents/{id}/files/1")).StatusCode.Should().Be(HttpStatusCode.NotFound, "already gone");
        (await UploadAsync(candidate, records, "x", "x.pdf", position: 1)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "can't replace a removed file");

        // Taking out the last file leaves the slot missing again, and a new file starts it off.
        await candidate.DeleteAsync($"/api/documents/{id}/files/2");
        var empty = await RecordsSlotAsync(candidate);
        empty.GetProperty("status").GetString().Should().Be("Missing");
        empty.GetProperty("files").GetArrayLength().Should().Be(0);
        await UploadAsync(candidate, records, "fresh", "fresh.pdf");
        FileNames(await RecordsSlotAsync(candidate)).Should().Equal("fresh.pdf");
    }

    [Fact]
    public async Task There_is_a_limit_and_single_file_documents_and_other_peoples_documents_are_protected()
    {
        var admin = await Flows.AdminAsync(api);
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var records = await TypeIdAsync(admin, "Academic records");
        var passport = await TypeIdAsync(admin, "Passport");

        Guid id = default;
        for (var i = 1; i <= 10; i++)
        {
            var response = await UploadAsync(candidate, records, $"page {i}", $"page-{i}.pdf");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            id = (await response.Read<JsonElement>()).GetProperty("id").GetGuid();
        }

        (await UploadAsync(candidate, records, "eleven", "page-11.pdf")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "ten files at most");
        (await UploadAsync(candidate, records, "swap", "swap.pdf", position: 3)).StatusCode.Should().Be(HttpStatusCode.OK, "replacing doesn't add a file");

        // A passport is one file: replacing works, a position other than 1 or removing doesn't.
        var passportId = (await (await UploadAsync(candidate, passport, "p1", "p1.pdf")).Read<JsonElement>()).GetProperty("id").GetGuid();
        (await UploadAsync(candidate, passport, "p2", "p2.pdf")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await UploadAsync(candidate, passport, "p3", "p3.pdf", position: 2)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await candidate.DeleteAsync($"/api/documents/{passportId}/files/1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Someone else's document can't be touched, and the staff can.
        (await other.DeleteAsync($"/api/documents/{id}/files/1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/documents/{id}/files/1")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await api.NewClient().DeleteAsync($"/api/documents/{id}/files/2")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_administrator_can_decide_which_document_types_take_several_files()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Extra " + Guid.NewGuid().ToString("N")[..8];
        var created = await (await admin.PostAsJsonAsync("/api/document-types", new { name, scope = "Candidate", tracksExpiry = false, allowsMultipleFiles = true })).Read<JsonElement>();
        created.GetProperty("allowsMultipleFiles").GetBoolean().Should().BeTrue();
        var id = created.GetProperty("id").GetGuid();

        var changed = await (await admin.PutAsJsonAsync($"/api/document-types/{id}", new { name, scope = "Candidate", tracksExpiry = false, isActive = true, allowsMultipleFiles = false })).Read<JsonElement>();
        changed.GetProperty("allowsMultipleFiles").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Staff_are_told_when_a_document_they_sent_back_comes_back_and_it_shows_in_todays_list()
    {
        var admin = await Flows.AdminAsync(api);
        var (reviewer, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var me = await (await candidate.GetAsync("/api/candidates/me")).Read<ProfileDto>();
        var records = await TypeIdAsync(admin, "Academic records");
        const string subject = "A student has sent a document again";

        var before = api.Emails.Sent.Count(m => m.Subject == subject);
        var id = (await (await UploadAsync(candidate, records, "grade 11", "grade-11.pdf")).Read<JsonElement>()).GetProperty("id").GetGuid();
        api.Emails.Sent.Count(m => m.Subject == subject).Should().Be(before, "a first upload isn't news, it is already in the review queue");

        await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "NeedsResubmission", note = "Grade 12 is missing." });
        var waiting = await (await reviewer.GetAsync("/api/briefing")).Read<JsonElement>();
        waiting.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("path").GetString() == $"/staff/candidates/{me.Id}", "nothing to decide until the student answers");

        // Two files in answer: staff hear about it once.
        await UploadAsync(candidate, records, "grade 12", "grade-12.pdf");
        await UploadAsync(candidate, records, "certificate", "certificate.pdf");
        var mail = api.Emails.Sent.Where(m => m.Subject == subject).Skip(before).ToList();
        mail.Should().ContainSingle();
        mail[0].TextBody.Should().Contain(me.FirstName).And.Contain("/staff/candidates/" + me.Id);
        mail[0].TextBody.Should().NotContain("Grade 12 is missing", "what to fix stays behind the sign-in");

        // And it is on every staff member's Today list until someone decides.
        var today = await (await reviewer.GetAsync("/api/briefing")).Read<JsonElement>();
        var item = today.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("path").GetString() == $"/staff/candidates/{me.Id}");
        item.GetProperty("kind").GetString().Should().Be("Document");
        item.GetProperty("title").GetString().Should().StartWith("Sent again:");
        item.GetProperty("urgency").GetString().Should().Be("Today");

        await reviewer.PostAsJsonAsync($"/api/documents/{id}/review", new { decision = "Accepted" });
        var after = await (await reviewer.GetAsync("/api/briefing")).Read<JsonElement>();
        after.GetProperty("items").EnumerateArray().Should().NotContain(i => i.GetProperty("path").GetString() == $"/staff/candidates/{me.Id}", "decided, so it is off the list");
    }
}
