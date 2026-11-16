using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Nichotan.Application.Common;
using Nichotan.Domain.Tasks;

namespace Nichotan.Tests.Integration;

// Tasks and the timeline reuse the visa set-up: a student with an accepted application and a visa case gives them plenty of history.
public partial class OutcomeTests
{
    private static string Day(int fromToday) => DateTime.UtcNow.AddDays(fromToday).ToString("yyyy-MM-dd");

    private async Task<Guid> OverdueTaskAsync(Guid assignee, string title, Guid? candidateId = null)
    {
        // The API refuses a due day in the past, so a task that has gone overdue is made the way time would: directly.
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var task = new StaffTask(title, null, TaskPriority.High, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), assignee, candidateId, null, null, assignee, "seed@example.test");
        db.StaffTasks.Add(task);
        await db.SaveChangesAsync();
        return task.Id;
    }

    [Fact]
    public async Task Staff_create_tasks_for_themselves_or_a_colleague_and_students_cannot_touch_them()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, me, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (_, colleague, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (student, _, _, _) = await Flows.RegisterCandidateAsync(api);

        var own = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Call the student", priority = "High", dueOn = Day(2) })).Read<JsonElement>();
        own.GetProperty("assigneeUserId").GetGuid().Should().Be(me.Id, "a task is yours unless you give it to someone");
        own.GetProperty("status").GetString().Should().Be("Open");
        own.GetProperty("overdue").GetBoolean().Should().BeFalse();

        var given = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Chase the PAL", assigneeUserId = colleague.Id })).Read<JsonElement>();
        given.GetProperty("assigneeUserId").GetGuid().Should().Be(colleague.Id);
        given.GetProperty("assigneeName").GetString().Should().Be("Staff Member");

        // Rejected: nothing to do, a due day in the past, someone who isn't staff, a link to nothing.
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", dueOn = Day(-1) })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", assigneeUserId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", candidateId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // The assignee list is staff only, and a disabled colleague can't be given work.
        var assignees = await (await counsellor.GetAsync("/api/tasks/assignees")).Read<JsonElement>();
        assignees.EnumerateArray().Should().Contain(a => a.GetProperty("id").GetGuid() == colleague.Id);
        await admin.PostAsync($"/api/staff/{colleague.Id}/disable", null);
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", assigneeUserId = colleague.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Students see none of it.
        (await student.GetAsync("/api/tasks")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.PostAsJsonAsync("/api/tasks", new { title = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await student.GetAsync("/api/tasks/summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.NewClient().GetAsync("/api/tasks")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tasks_list_the_overdue_ones_first_and_can_be_filtered_completed_reopened_and_edited()
    {
        var admin = await Flows.AdminAsync(api);
        var (counsellor, me, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");
        var (other, _, _, _) = await Flows.NewStaffAsync(api, admin, "AdmissionsCounsellor");

        var later = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Later", priority = "Low", dueOn = Day(20) })).Read<JsonElement>();
        var soon = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Soon", priority = "Normal", dueOn = Day(1) })).Read<JsonElement>();
        var overdueId = await OverdueTaskAsync(me.Id, "Overdue one");
        var undated = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "No date" })).Read<JsonElement>();

        async Task<string[]> Titles(string url) => (await (await counsellor.GetAsync(url)).Read<JsonElement>()).GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).ToArray();
        var mine = await Titles("/api/tasks?mine=true");
        mine.Should().Equal("Overdue one", "Soon", "Later", "No date");
        (await Titles("/api/tasks?mine=true&overdueOnly=true")).Should().Equal("Overdue one");
        (await (await counsellor.GetAsync("/api/tasks?mine=true")).Read<JsonElement>()).GetProperty("items")[0].GetProperty("overdue").GetBoolean().Should().BeTrue();

        var summary = await (await counsellor.GetAsync("/api/tasks/summary")).Read<JsonElement>();
        summary.GetProperty("mineOpen").GetInt32().Should().Be(4);
        summary.GetProperty("mineOverdue").GetInt32().Should().Be(1);
        summary.GetProperty("allOverdue").GetInt32().Should().BeGreaterThanOrEqualTo(1);

        // Anyone on the staff can finish it, and the record says who.
        var done = await (await other.PostAsJsonAsync($"/api/tasks/{soon.GetProperty("id").GetGuid()}/status", new { action = "complete" })).Read<JsonElement>();
        done.GetProperty("status").GetString().Should().Be("Done");
        done.GetProperty("completedBy").GetString().Should().Contain("@");
        (await Titles("/api/tasks?mine=true")).Should().NotContain("Soon");
        (await Titles("/api/tasks?mine=true&status=Done")).Should().Equal("Soon");
        (await other.PostAsJsonAsync($"/api/tasks/{soon.GetProperty("id").GetGuid()}/status", new { action = "complete" })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "already done");
        (await counsellor.PutAsJsonAsync($"/api/tasks/{soon.GetProperty("id").GetGuid()}", new { title = "Edited", priority = "Low", assigneeUserId = me.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "reopen it before changing it");
        (await counsellor.PostAsJsonAsync($"/api/tasks/{soon.GetProperty("id").GetGuid()}/status", new { action = "reopen" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await counsellor.PostAsJsonAsync($"/api/tasks/{soon.GetProperty("id").GetGuid()}/status", new { action = "nonsense" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Editing: reassign and reword; a due day already past can stay, but a new one can't be set in the past.
        var edited = await (await counsellor.PutAsJsonAsync($"/api/tasks/{overdueId}", new { title = "Overdue, now with notes", notes = "Spoke to the school", priority = "High", dueOn = Day(-3), assigneeUserId = me.Id })).Read<JsonElement>();
        edited.GetProperty("notes").GetString().Should().Be("Spoke to the school");
        (await counsellor.PutAsJsonAsync($"/api/tasks/{overdueId}", new { title = "x", priority = "High", dueOn = Day(-9), assigneeUserId = me.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await counsellor.PostAsJsonAsync($"/api/tasks/{undated.GetProperty("id").GetGuid()}/status", new { action = "cancel" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Titles("/api/tasks?mine=true&status=Cancelled")).Should().Equal("No date");
        _ = later;
    }

    [Fact]
    public async Task A_task_can_be_tied_to_a_student_and_their_case_and_overdue_ones_reach_the_daily_digest()
    {
        var (admin, counsellor, _, candidateId, applicationId, visaId) = await WithVisaCaseAsync();
        var (other, _, _, _) = await Flows.RegisterCandidateAsync(api);
        var otherId = (await (await other.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Id;

        var linked = await (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Check the funds letter", visaCaseId = visaId })).Read<JsonElement>();
        linked.GetProperty("candidateId").GetGuid().Should().Be(candidateId, "a case brings its student with it");
        linked.GetProperty("candidateName").GetString().Should().NotBeNullOrWhiteSpace();
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", applicationId, candidateId = otherId })).StatusCode.Should().Be(HttpStatusCode.BadRequest, "that application is someone else's");
        (await counsellor.PostAsJsonAsync("/api/tasks", new { title = "x", visaCaseId = visaId, candidateId = otherId })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var forStudent = await (await counsellor.GetAsync($"/api/tasks?candidateId={candidateId}")).Read<JsonElement>();
        forStudent.GetProperty("items").EnumerateArray().Should().ContainSingle(i => i.GetProperty("title").GetString() == "Check the funds letter");
        (await (await counsellor.GetAsync($"/api/tasks?visaCaseId={visaId}")).Read<JsonElement>()).GetProperty("total").GetInt32().Should().Be(1);

        // A task past its due day is counted in the day's staff digest.
        var me = await (await admin.GetAsync("/api/auth/me")).Read<MeDto>();
        await OverdueTaskAsync(me.Id, "Long overdue");
        var before = Received(api.Emails, "enquiries@nichotan.com", "Follow-ups are overdue");
        await RunRemindersAsync(DateTime.UtcNow.Date.AddDays(30).AddHours(9));
        Received(api.Emails, "enquiries@nichotan.com", "Follow-ups are overdue").Should().Be(before + 1);
        var digest = api.Emails.Sent.Last(m => m.To == "enquiries@nichotan.com" && m.Subject == "Follow-ups are overdue");
        digest.TextBody.Should().Contain("task");
        digest.TextBody.Should().NotContain("Long overdue", "counts only, never the task's words");
    }

    [Fact]
    public async Task The_timeline_gathers_what_happened_newest_first_for_staff_only()
    {
        var (admin, counsellor, student, candidateId, applicationId, visaId) = await WithVisaCaseAsync();
        var passport = await TypeIdByNameAsync(admin, "Passport");
        (await UploadSharedAsync(student, passport)).StatusCode.Should().Be(HttpStatusCode.OK);
        await counsellor.PostAsJsonAsync("/api/tasks", new { title = "Timeline task", candidateId });
        await MoveVisaAsync(counsellor, visaId, "Submitted");

        var timeline = await (await counsellor.GetAsync($"/api/candidates/{candidateId}/timeline")).Read<JsonElement>();
        var titles = timeline.EnumerateArray().Select(e => e.GetProperty("title").GetString()!).ToList();
        titles.Should().Contain("Registered");
        titles.Should().Contain(t => t.EndsWith(": Offer received"));
        titles.Should().Contain(t => t.Contains("formal acceptance recorded"));
        titles.Should().Contain("Visa case opened").And.Contain("Visa: Submitted");
        titles.Should().Contain("Passport uploaded").And.Contain("Task added: Timeline task");

        // Newest first, so the visa move just made is above the registration at the bottom.
        titles.IndexOf("Visa: Submitted").Should().BeLessThan(titles.IndexOf("Registered"));
        timeline.EnumerateArray().Select(e => e.GetProperty("atUtc").GetDateTime()).Should().BeInDescendingOrder();
        var step = timeline.EnumerateArray().First(e => e.GetProperty("title").GetString() == "Visa: Submitted");
        step.GetProperty("by").GetString().Should().Contain("@");
        step.GetProperty("linkType").GetString().Should().Be("visa");
        step.GetProperty("linkId").GetGuid().Should().Be(visaId);
        _ = applicationId;

        (await student.GetAsync($"/api/candidates/{candidateId}/timeline")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await counsellor.GetAsync($"/api/candidates/{Guid.NewGuid()}/timeline")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var audit = await (await admin.GetAsync($"/api/audit-events?entityType=CandidateProfile&entityId={candidateId}&pageSize=50")).Read<PageDto<AuditDto>>();
        audit.Items.Should().Contain(e => e.Action == "CandidateTimelineViewed", "opening a whole history is audited");
    }

    [Fact]
    public async Task Opening_WhatsApp_with_a_students_list_is_recorded_for_staff_only()
    {
        var (admin, counsellor, student, candidateId, _, _) = await WithVisaCaseAsync();

        (await counsellor.PostAsJsonAsync($"/api/candidates/{candidateId}/whatsapp-shares", new { topic = "pre-departure" })).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var audit = await (await admin.GetAsync($"/api/audit-events?entityType=CandidateProfile&entityId={candidateId}&pageSize=50")).Read<PageDto<AuditDto>>();
        audit.Items.Should().Contain(e => e.Action == "WhatsAppShareOpened" && e.Details!.Contains("pre-departure"));

        (await counsellor.PostAsJsonAsync($"/api/candidates/{candidateId}/whatsapp-shares", new { topic = "anything else" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await counsellor.PostAsJsonAsync($"/api/candidates/{Guid.NewGuid()}/whatsapp-shares", new { topic = "documents" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await student.PostAsJsonAsync($"/api/candidates/{candidateId}/whatsapp-shares", new { topic = "documents" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
