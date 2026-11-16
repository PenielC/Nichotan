using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Nichotan.Application.Briefing;

namespace Nichotan.Tests.Integration;

public class BriefingRulesTests
{
    private static BriefingItem Item(string kind, BriefingUrgency urgency, string title, int late = 0) => new(kind, urgency, title, "", "/x", late);

    [Fact]
    public void Items_are_ordered_by_urgency_then_how_late_then_kind_then_title()
    {
        var ranked = BriefingRules.Rank(
        [
            Item(BriefingRules.Task, BriefingUrgency.Soon, "Soon task"),
            Item(BriefingRules.Task, BriefingUrgency.Overdue, "Task 2 days late", 2),
            Item(BriefingRules.Pal, BriefingUrgency.Overdue, "PAL 2 days late", 2),
            Item(BriefingRules.Biometrics, BriefingUrgency.Overdue, "Biometrics 2 days late", 2),
            Item(BriefingRules.Task, BriefingUrgency.Overdue, "Task 9 days late", 9),
            Item(BriefingRules.Task, BriefingUrgency.Today, "Today task"),
        ]);

        ranked.Select(i => i.Title).Should().Equal("Task 9 days late", "Biometrics 2 days late", "PAL 2 days late", "Task 2 days late", "Today task", "Soon task");
    }

    [Theory]
    [InlineData(0, 0, 0, "Nothing needs your attention today.")]
    [InlineData(1, 0, 0, "1 thing needs your attention: 1 overdue.")]
    [InlineData(4, 1, 2, "7 things need your attention: 4 overdue, 1 due today, 2 coming up.")]
    public void The_headline_counts_each_tier_and_leaves_out_empty_ones(int overdue, int today, int soon, string expected) =>
        BriefingRules.Headline(overdue, today, soon).Should().Be(expected);

    [Fact]
    public void A_task_is_overdue_today_or_coming_up_by_its_due_day()
    {
        var today = new DateOnly(2026, 11, 16);
        BriefingRules.TaskUrgency(today.AddDays(-1), today).Should().Be(BriefingUrgency.Overdue);
        BriefingRules.TaskUrgency(today, today).Should().Be(BriefingUrgency.Today);
        BriefingRules.TaskUrgency(today.AddDays(2), today).Should().Be(BriefingUrgency.Soon);
    }
}

[Collection(ApiCollection.Name)]
public class BriefingTests(ApiFactory api)
{
    private static string Day(int fromToday) => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(fromToday).ToString("yyyy-MM-dd");

    [Fact]
    public async Task The_briefing_has_my_own_tasks_that_are_due_soon_and_not_a_colleagues_or_far_off_ones()
    {
        var admin = await Flows.AdminAsync(api);
        var (mine, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (other, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var tag = Guid.NewGuid().ToString("N")[..8];

        await mine.PostAsJsonAsync("/api/tasks", new { title = $"Due today {tag}", dueOn = Day(0) });
        await mine.PostAsJsonAsync("/api/tasks", new { title = $"Due in two days {tag}", dueOn = Day(2) });
        await mine.PostAsJsonAsync("/api/tasks", new { title = $"Due next month {tag}", dueOn = Day(30) });
        await mine.PostAsJsonAsync("/api/tasks", new { title = $"No due day {tag}" });
        await other.PostAsJsonAsync("/api/tasks", new { title = $"Colleague's {tag}", dueOn = Day(0) });

        var result = await (await mine.GetAsync("/api/briefing")).Read<JsonElement>();
        var titles = result.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).Where(t => t.EndsWith(tag)).ToList();
        titles.Should().Equal($"Due today {tag}", $"Due in two days {tag}");
        result.GetProperty("today").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        var theirs = await (await other.GetAsync("/api/briefing")).Read<JsonElement>();
        theirs.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).Should().NotContain(t => t.StartsWith("Due") && t.EndsWith(tag), "a task belongs to its assignee");
    }

    [Fact]
    public async Task Only_staff_can_open_the_briefing()
    {
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);
        (await student.GetAsync("/api/briefing")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync("/api/briefing")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
