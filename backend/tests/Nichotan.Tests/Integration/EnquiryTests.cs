using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class EnquiryTests(ApiFactory api)
{
    private static object Enquiry(string name, string? email = "ask@example.test", string? phone = null, string? programme = "Nursing", bool guidance = false, int year = 2022, string? website = null) => new
    {
        name,
        email,
        phone,
        highestQualification = "Diploma",
        graduationYear = year,
        preferredProgramme = programme,
        openToGuidance = guidance,
        destinationInterest = "Both",
        message = "Hello",
        website,
    };

    [Fact]
    public async Task Anyone_can_send_an_enquiry_without_an_account_and_staff_can_see_it()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Enquirer " + Guid.NewGuid().ToString("N")[..8];

        (await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry(name))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var list = await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>();
        var item = list.GetProperty("items").EnumerateArray().Single();
        item.GetProperty("status").GetString().Should().Be("New");
        item.GetProperty("highestQualification").GetString().Should().Be("Diploma");
        item.GetProperty("destinationInterest").GetString().Should().Be("Both");
        item.GetProperty("preferredProgramme").GetString().Should().Be("Nursing");
    }

    [Fact]
    public async Task An_enquiry_needs_a_way_to_reach_the_person_and_to_know_what_they_want()
    {
        var anon = api.NewClient();
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("No Contact", email: null, phone: null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("Phone Only", email: null, phone: "+263 71 612 4255"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("No Programme", programme: null, guidance: false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("Wants Guidance", programme: null, guidance: true))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("Bad Email", email: "not-an-email"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("Bad Year", year: 1800))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries", Enquiry("Bad Phone", phone: "call me maybe"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_bot_that_fills_the_hidden_field_looks_successful_but_nothing_is_saved()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Bot " + Guid.NewGuid().ToString("N")[..8];

        (await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry(name, website: "http://spam.example"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static object WebsiteMessage(string name, string? email = "visitor@example.test", string? phone = null, string? message = "I would like to study in Canada.", string? website = null) => new
    {
        name,
        email,
        phone,
        message,
        website,
    };

    [Fact]
    public async Task A_message_from_the_website_form_lands_in_the_enquiries_list_marked_with_where_it_came_from()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Website Visitor " + Guid.NewGuid().ToString("N")[..8];

        (await api.NewClient().PostAsJsonAsync("/api/enquiries/website", WebsiteMessage(name))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var item = (await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items").EnumerateArray().Single();
        item.GetProperty("source").GetString().Should().Be("WebsiteContactForm");
        item.GetProperty("status").GetString().Should().Be("New");
        item.GetProperty("message").GetString().Should().Be("I would like to study in Canada.");
        item.GetProperty("email").GetString().Should().Be("visitor@example.test");
        // The short form does not ask for these, so they are empty rather than invented.
        item.GetProperty("highestQualification").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("graduationYear").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("destinationInterest").ValueKind.Should().Be(JsonValueKind.Null);
        // Staff can open it like any other enquiry.
        (await admin.PostAsync($"/api/enquiries/{item.GetProperty("id").GetGuid()}/viewed", null)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Enquiries_from_the_landing_page_are_marked_as_such()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Landing " + Guid.NewGuid().ToString("N")[..8];
        await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry(name));

        (await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items")[0].GetProperty("source").GetString().Should().Be("LandingPage");
    }

    [Fact]
    public async Task A_website_message_needs_a_name_a_way_to_reach_the_person_and_some_words()
    {
        var anon = api.NewClient();
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("", email: "a@example.test"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("No Contact", email: null, phone: null))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("No Words", message: "  "))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("Bad Email", email: "not-an-email"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("Too Long", message: new string('x', 2001)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await anon.PostAsJsonAsync("/api/enquiries/website", WebsiteMessage("Phone Only", email: null, phone: "+263 71 612 4255"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_bot_that_fills_the_hidden_field_on_the_website_form_looks_successful_but_nothing_is_saved()
    {
        var admin = await Flows.AdminAsync(api);
        var name = "Web Bot " + Guid.NewGuid().ToString("N")[..8];

        (await api.NewClient().PostAsJsonAsync("/api/enquiries/website", WebsiteMessage(name, website: "http://spam.example"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await (await admin.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Only_staff_read_and_update_enquiries_and_students_and_visitors_cannot()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (candidate, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var name = "Followed Up " + Guid.NewGuid().ToString("N")[..8];
        await api.NewClient().PostAsJsonAsync("/api/enquiries", Enquiry(name));
        var id = (await (await counsellor.GetAsync($"/api/enquiries?search={name}")).Read<JsonElement>()).GetProperty("items")[0].GetProperty("id").GetGuid();

        (await api.NewClient().GetAsync("/api/enquiries")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await candidate.GetAsync("/api/enquiries")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await candidate.PutAsJsonAsync($"/api/enquiries/{id}", new { status = "Contacted" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var updated = await (await counsellor.PutAsJsonAsync($"/api/enquiries/{id}", new { status = "Contacted", staffNote = "Called, sending the brochure." })).Read<JsonElement>();
        updated.GetProperty("status").GetString().Should().Be("Contacted");
        updated.GetProperty("staffNote").GetString().Should().Contain("brochure");
        updated.GetProperty("handledBy").GetString().Should().Contain("@");
        (await (await admin.GetAsync("/api/enquiries?status=New&search=" + name)).Read<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(0);
    }
}
