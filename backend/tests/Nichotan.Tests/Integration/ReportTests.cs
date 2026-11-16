using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;

namespace Nichotan.Tests.Integration;

[Collection(ApiCollection.Name)]
public class ReportTests(ApiFactory api)
{
    private static readonly int Year = DateTime.UtcNow.Year + 1;

    private static int Count(JsonElement report, string block, string status) =>
        report.GetProperty(block).EnumerateArray().Single(s => s.GetProperty("status").GetString() == status).GetProperty("count").GetInt32();

    [Fact]
    public async Task A_past_month_is_rebuilt_from_the_dates_things_happened_and_pipeline_and_movement_are_separate()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var institution = await (await counsellor.PostAsJsonAsync("/api/institutions", new { name = "Report " + Guid.NewGuid().ToString("N")[..8], country = "Canada" })).Read<JsonElement>();
        var institutionId = institution.GetProperty("id").GetGuid();
        var programme = await (await counsellor.PostAsJsonAsync("/api/programmes", new { institutionId, name = "Study", level = "Undergraduate", intakes = new[] { new { term = "Fall", year = Year } } })).Read<JsonElement>();
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var created = await (await student.PostAsJsonAsync("/api/admissions/mine", new { programmeId = programme.GetProperty("id").GetGuid(), intakeTerm = "Fall", intakeYear = Year })).Read<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        // Staff record that it became ready for review in the middle of LAST month, entering it today.
        var today = DateTime.UtcNow;
        var lastMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1);
        var day = lastMonth.AddDays(14).ToString("yyyy-MM-dd");
        (await counsellor.PostAsJsonAsync($"/api/admissions/{id}/status", new { toStatus = "ReadyForReview", occurredOn = day, acknowledgeDocumentGaps = true })).StatusCode.Should().Be(HttpStatusCode.OK);

        var thisMonth = today.ToString("yyyy-MM");
        var last = lastMonth.ToString("yyyy-MM");
        string Url(string month) => $"/api/reports/monthly?month={month}&institutionId={institutionId}";

        // Last month: the move happened then, so it is movement in last month and where the application stood at its end.
        var before = await (await counsellor.GetAsync(Url(last))).Read<JsonElement>();
        Count(before, "admissionMovement", "ReadyForReview").Should().Be(1);
        Count(before, "admissionPipeline", "ReadyForReview").Should().Be(1);
        Count(before, "admissionPipeline", "Draft").Should().Be(0, "it was only created (and entered) this month");
        before.GetProperty("asAt").GetString().Should().Be(lastMonth.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"));

        // This month: it was created now (movement into Draft) and stands at Ready for review; nothing moved into Ready for review this month.
        var now = await (await counsellor.GetAsync(Url(thisMonth))).Read<JsonElement>();
        Count(now, "admissionMovement", "Draft").Should().Be(1);
        Count(now, "admissionMovement", "ReadyForReview").Should().Be(0, "that happened last month, even though it was entered this month");
        Count(now, "admissionPipeline", "ReadyForReview").Should().Be(1);
        now.GetProperty("asAt").GetString().Should().Be(today.ToString("yyyy-MM-dd"), "the month still running is measured to today");

        // Every status is listed, so the table doesn't change shape.
        now.GetProperty("admissionPipeline").GetArrayLength().Should().BeGreaterThan(8);

        // Filters narrow it: another country has none of this.
        var other = await (await counsellor.GetAsync($"/api/reports/monthly?month={thisMonth}&institutionId={institutionId}&country=Germany")).Read<JsonElement>();
        Count(other, "admissionPipeline", "ReadyForReview").Should().Be(0);
        var level = await (await counsellor.GetAsync($"/api/reports/monthly?month={thisMonth}&institutionId={institutionId}&level=Undergraduate&intakeYear={Year}&intakeTerm=Fall")).Read<JsonElement>();
        Count(level, "admissionPipeline", "ReadyForReview").Should().Be(1);

        // Conversions are cohort based and say how they are worked out.
        var conversion = now.GetProperty("conversions").EnumerateArray().First();
        conversion.GetProperty("definition").GetString().Should().Contain("during the month");
        conversion.GetProperty("cohort").GetInt32().Should().Be(0, "nothing was submitted to the school");
    }

    [Fact]
    public async Task Reports_are_for_staff_not_students_and_a_month_must_be_a_real_month_that_has_started()
    {
        var admin = await Flows.AdminAsync(api);
        var url = $"/api/reports/monthly?month={DateTime.UtcNow:yyyy-MM}";
        (await admin.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);

        var (counsellor, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var seen = await counsellor.GetAsync(url);
        seen.StatusCode.Should().Be(HttpStatusCode.OK, "counsellors read reports");
        (await seen.Content.ReadAsStringAsync()).Should().NotContain("@", "no individual's details are in a report");

        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await student.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.GetAsync("/api/reports/monthly?month=2026-13")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/reports/monthly?month=nonsense")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.GetAsync($"/api/reports/monthly?month={DateTime.UtcNow.AddMonths(2):yyyy-MM}")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "that month hasn't started");
    }
}
