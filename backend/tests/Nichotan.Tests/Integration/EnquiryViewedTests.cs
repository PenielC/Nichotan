using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class EnquiryViewedTests(ApiFactory api)
{
    private async Task<(Guid Id, string Name)> NewEnquiryAsync(HttpClient staff)
    {
        var name = "Viewed Test " + Guid.NewGuid().ToString("N")[..8];
        await api.NewClient().PostAsJsonAsync("/api/enquiries", new { name, email = "v@example.test", highestQualification = "Diploma", graduationYear = 2022, preferredProgramme = "Nursing", openToGuidance = false, destinationInterest = "Both" });
        var item = (await (await staff.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items")[0];
        return (item.GetProperty("id").GetGuid(), name);
    }

    [Fact]
    public async Task A_new_enquiry_emails_the_team_a_link_and_does_not_email_the_sender()
    {
        var name = "Notify Test " + Guid.NewGuid().ToString("N")[..8];
        var sender = $"sender.{Guid.NewGuid():N}@example.test";
        await api.NewClient().PostAsJsonAsync("/api/enquiries", new { name, email = sender, phone = "+263 71 000 0000", highestQualification = "Diploma", graduationYear = 2022, preferredProgramme = "Secret Programme", openToGuidance = false, destinationInterest = "Both", message = "private message" });

        var mail = api.Emails.Sent.Single(m => m.To == "enquiries@nichotan.com" && m.TextBody.Contains(name));
        mail.Subject.Should().Be("New enquiry on the Nichotan website");
        mail.TextBody.Should().Contain("https://web.test/staff/enquiries");
        mail.TextBody.Should().NotContain(sender).And.NotContain("+263 71 000 0000").And.NotContain("Secret Programme").And.NotContain("private message", "the email carries a link, not the person's details");
        api.Emails.Sent.Should().NotContain(m => m.To == sender, "the sender isn't sent a confirmation");

        // A bot caught by the trap field doesn't wake the team.
        var botName = "Notify Bot " + Guid.NewGuid().ToString("N")[..8];
        await api.NewClient().PostAsJsonAsync("/api/enquiries", new { name = botName, email = sender, highestQualification = "Diploma", graduationYear = 2022, preferredProgramme = "x", openToGuidance = false, destinationInterest = "Both", website = "http://spam.example" });
        api.Emails.Sent.Should().NotContain(m => m.TextBody.Contains(botName));

        // Someone typing line breaks into their name can't add lines to the email.
        await api.NewClient().PostAsJsonAsync("/api/enquiries", new { name = "Line\r\nBreak Name", email = sender, highestQualification = "Diploma", graduationYear = 2022, preferredProgramme = "x", openToGuidance = false, destinationInterest = "Both" });
        api.Emails.Sent.Last(m => m.To == "enquiries@nichotan.com").TextBody.Should().Contain("LineBreak Name has sent an enquiry");
    }

    [Fact]
    public async Task A_new_enquiry_is_unviewed_until_an_admin_or_counsellor_opens_it_and_the_first_viewer_is_kept()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, counsellorEmail, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (id, name) = await NewEnquiryAsync(admin);

        var before = await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>();
        before.GetProperty("items")[0].GetProperty("viewedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
        var unviewedBefore = (await (await admin.GetAsync("/api/enquiries/summary")).Read<JsonElement>()).GetProperty("unviewed").GetInt32();
        unviewedBefore.Should().BeGreaterThan(0);

        var viewed = await (await counsellor.PostAsync($"/api/enquiries/{id}/viewed", null)).Read<JsonElement>();
        viewed.GetProperty("viewedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        viewed.GetProperty("viewedBy").GetString().Should().Be(counsellorEmail);

        // A second person opening it doesn't take the credit.
        var again = await (await admin.PostAsync($"/api/enquiries/{id}/viewed", null)).Read<JsonElement>();
        again.GetProperty("viewedBy").GetString().Should().Be(counsellorEmail);
        (await (await admin.GetAsync("/api/enquiries/summary")).Read<JsonElement>()).GetProperty("unviewed").GetInt32().Should().Be(unviewedBefore - 1);
        (await (await admin.GetAsync($"/api/enquiries?unviewedOnly=true&search={name}")).Read<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Only_staff_can_mark_an_enquiry_viewed_and_the_state_shows_who_looked()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var (id, name) = await NewEnquiryAsync(admin);

        (await candidate.PostAsync($"/api/enquiries/{id}/viewed", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().PostAsync($"/api/enquiries/{id}/viewed", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await counsellor.GetAsync("/api/enquiries/summary")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await candidate.GetAsync("/api/enquiries/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await admin.PostAsync($"/api/enquiries/{id}/viewed", null);
        (await (await counsellor.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items")[0].GetProperty("viewedBy").GetString().Should().Contain("@");
        (await admin.PostAsync($"/api/enquiries/{Guid.NewGuid()}/viewed", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
