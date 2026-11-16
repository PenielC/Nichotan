using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

// The pre-departure list hangs off an approved visa case, so it reuses the visa set-up.
public partial class OutcomeTests
{
    private async Task<(HttpClient Admin, HttpClient Counsellor, HttpClient Student, Guid VisaId)> WithApprovedVisaAsync(string country = "Canada")
    {
        var (admin, counsellor, student, _, _, visaId) = await WithVisaCaseAsync(country);
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        await MoveVisaAsync(counsellor, visaId, "Approved");
        return (admin, counsellor, student, visaId);
    }

    private static string[] ItemTitles(JsonElement list) => list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();

    private static JsonElement ItemNamed(JsonElement list, string title) => list.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("title").GetString() == title);

    [Fact]
    public async Task The_pre_departure_list_opens_after_approval_from_the_countrys_own_template()
    {
        var (_, counsellor, student, _, _, visaId) = await WithVisaCaseAsync();

        // Not before the visa has been approved.
        (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        await MoveVisaAsync(counsellor, visaId, "Approved");

        (await student.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var before = await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/pre-departure")).Read<JsonElement>();
        before.GetProperty("activated").GetBoolean().Should().BeFalse();
        before.GetProperty("canActivate").GetBoolean().Should().BeTrue();

        var list = await (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).Read<JsonElement>();
        ItemTitles(list).Should().Contain(["Passport with your visa", "Study permit approval letter", "Proof of funds", "Warm clothing"]);
        ItemNamed(list, "Warm clothing").GetProperty("importance").GetString().Should().Be("Recommended", "clothing is advice, not a requirement");
        ItemNamed(list, "Passport with your visa").GetProperty("importance").GetString().Should().Be("Required");
        list.GetProperty("caveat").GetString().Should().NotBeNullOrWhiteSpace();
        api.Emails.Sent.Should().Contain(m => m.Subject == "Your pre-departure checklist is ready" && m.TextBody.Contains("/portal/applications"));
        (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already active");

        // Germany has its own list: Canada's isn't assumed.
        var germany = await WithApprovedVisaAsync("Germany");
        var german = await (await germany.Counsellor.PostAsync($"/api/visa-cases/{germany.VisaId}/pre-departure", null)).Read<JsonElement>();
        ItemTitles(german).Should().Contain(["Proof of health insurance", "Admission letter from your university"]);
        ItemTitles(german).Should().NotContain("Study permit approval letter");
    }

    [Fact]
    public async Task The_student_confirms_each_item_staff_verify_it_and_the_list_is_ready_when_the_required_ones_are_checked()
    {
        var (_, counsellor, student, visaId) = await WithApprovedVisaAsync();
        var list = await (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).Read<JsonElement>();
        list.GetProperty("readyToTravel").GetBoolean().Should().BeFalse();

        var mine = (await (await student.GetAsync("/api/visa-cases/mine/pre-departure")).Read<JsonElement>()).EnumerateArray().Single();
        var passport = ItemNamed(mine, "Passport with your visa").GetProperty("id").GetGuid();

        // Confirming is the student's; checking is staff's.
        var confirmed = await (await student.PutAsJsonAsync($"/api/visa-cases/mine/pre-departure/items/{passport}/confirm", new { confirmed = true })).Read<JsonElement>();
        ItemNamed(confirmed, "Passport with your visa").GetProperty("confirmedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        ItemNamed(confirmed, "Passport with your visa").GetProperty("completedAtUtc").ValueKind.Should().Be(JsonValueKind.Null, "staff haven't checked it");
        (await student.PostAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items/{passport}/verify", new { verified = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await other.PutAsJsonAsync($"/api/visa-cases/mine/pre-departure/items/{passport}/confirm", new { confirmed = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await (await other.GetAsync("/api/visa-cases/mine/pre-departure")).Read<JsonElement>()).GetArrayLength().Should().Be(0);

        // Staff check every required item; the recommended one doesn't hold the list up.
        var current = await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/pre-departure")).Read<JsonElement>();
        foreach (var item in current.GetProperty("items").EnumerateArray().Where(i => i.GetProperty("importance").GetString() == "Required"))
        {
            current = await (await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items/{item.GetProperty("id").GetGuid()}/verify", new { verified = true })).Read<JsonElement>();
        }

        current.GetProperty("readyToTravel").GetBoolean().Should().BeTrue();
        var verified = ItemNamed(current, "Passport with your visa");
        verified.GetProperty("verifiedBy").GetString().Should().Contain("@");
        verified.GetProperty("completedAtUtc").ValueKind.Should().Be(JsonValueKind.String);
        (await student.PutAsJsonAsync($"/api/visa-cases/mine/pre-departure/items/{passport}/confirm", new { confirmed = false })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "staff have checked it");

        // Undoing a check puts the list back to not ready.
        var undone = await (await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items/{passport}/verify", new { verified = false })).Read<JsonElement>();
        undone.GetProperty("readyToTravel").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Staff_can_add_notes_and_switch_off_items_and_either_side_can_set_the_departure_date()
    {
        var (_, counsellor, student, visaId) = await WithApprovedVisaAsync();
        var list = await (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).Read<JsonElement>();
        var medical = ItemNamed(list, "Medical information sheet").GetProperty("id").GetGuid();

        var added = await (await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items", new { title = "Printed bank letter", detail = "From your bank", importance = "Required" })).Read<JsonElement>();
        ItemNamed(added, "Printed bank letter").GetProperty("source").GetString().Should().Be("Manual");
        (await counsellor.PostAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items", new { title = "", importance = "Required" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Switching an item off hides it from the student and from "ready".
        var off = await (await counsellor.PutAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/items/{medical}", new { note = "Not asked for in your case", importance = "Required", isActive = false })).Read<JsonElement>();
        ItemNamed(off, "Medical information sheet").GetProperty("isActive").GetBoolean().Should().BeFalse();
        var mine = (await (await student.GetAsync("/api/visa-cases/mine/pre-departure")).Read<JsonElement>()).EnumerateArray().Single();
        ItemTitles(mine).Should().NotContain("Medical information sheet").And.Contain("Printed bank letter");
        (await student.PutAsJsonAsync($"/api/visa-cases/mine/pre-departure/items/{medical}/confirm", new { confirmed = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Departure date: not in the past; the student or staff can set it.
        (await student.PutAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/departure", new { plannedOn = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd") })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var day = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd");
        (await (await student.PutAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/departure", new { plannedOn = day })).Read<JsonElement>()).GetProperty("plannedDepartureOn").GetString().Should().Be(day);
        var later = DateTime.UtcNow.AddDays(40).ToString("yyyy-MM-dd");
        (await (await counsellor.PutAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/departure", new { plannedOn = later })).Read<JsonElement>()).GetProperty("plannedDepartureOn").GetString().Should().Be(later);
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await other.PutAsJsonAsync($"/api/visa-cases/{visaId}/pre-departure/departure", new { plannedOn = day })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_administrators_edit_the_country_templates_and_edits_leave_activated_lists_alone()
    {
        var (admin, counsellor, _, visaId) = await WithApprovedVisaAsync();
        var list = await (await counsellor.PostAsync($"/api/visa-cases/{visaId}/pre-departure", null)).Read<JsonElement>();
        var itemCount = list.GetProperty("items").GetArrayLength();

        var body = new { country = "Canada", title = "Offline maps", detail = "Download before you fly", importance = "Recommended", sortOrder = 500 };
        (await counsellor.PostAsJsonAsync("/api/pre-departure-template/items", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var created = await (await admin.PostAsJsonAsync("/api/pre-departure-template/items", body)).Read<JsonElement>();
        (await admin.PutAsJsonAsync($"/api/pre-departure-template/items/{created.GetProperty("id").GetGuid()}", new { country = "Germany", title = "Offline maps", importance = "Recommended", sortOrder = 500 })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "an item can't change country");

        (await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/pre-departure")).Read<JsonElement>()).GetProperty("items").GetArrayLength().Should().Be(itemCount, "lists already activated keep their items");
        var later = await WithApprovedVisaAsync();
        ItemTitles(await (await later.Counsellor.PostAsync($"/api/visa-cases/{later.VisaId}/pre-departure", null)).Read<JsonElement>()).Should().Contain("Offline maps");

        // The wording shown with the list is also the administrators'.
        (await counsellor.PutAsJsonAsync("/api/pre-departure-template/caveats/Canada", new { text = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PutAsJsonAsync("/api/pre-departure-template/caveats/Canada", new { text = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await (await admin.PutAsJsonAsync("/api/pre-departure-template/caveats/Canada", new { text = "Approved wording." })).Read<JsonElement>()).GetProperty("text").GetString().Should().Be("Approved wording.");
        (await (await counsellor.GetAsync($"/api/visa-cases/{visaId}/pre-departure")).Read<JsonElement>()).GetProperty("caveat").GetString().Should().Be("Approved wording.");
    }
}
