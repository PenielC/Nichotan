using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Admissions;
using Nichotan.Application.Common;
using Nichotan.Application.Documents;
using Nichotan.Application.Visas;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Tasks;
using Nichotan.Domain.Visas;

namespace Nichotan.Application.Briefing;

public enum BriefingUrgency { Overdue, Today, Soon }

/// <summary>One thing to look at: what it is, how urgent, and where to go to deal with it.</summary>
public record BriefingItem(string Kind, BriefingUrgency Urgency, string Title, string Detail, string Path, int DaysLate);

public record BriefingResult(string Headline, int Overdue, int Today, int Soon, IReadOnlyList<BriefingItem> Items);

/// <summary>
/// The rules behind "Today" (BR-10.7). Nothing here is guessed: each item comes from a recorded date and a stated limit, and the order
/// is fixed, so the same data always gives the same briefing.
/// </summary>
public static class BriefingRules
{
    public const string Biometrics = "Biometrics";
    public const string Pal = "Pal";
    public const string Document = "Document";
    public const string Task = "Task";

    /// <summary>A task due within this many days (after today) is shown as coming up.</summary>
    public const int TaskSoonDays = 3;

    /// <summary>A document the student has sent back after staff asked for fixes is overdue for a decision after this many days.</summary>
    public const int ResubmittedOverdueAfterDays = 2;

    /// <summary>Most items listed; the counts always cover everything.</summary>
    public const int MaxItems = 30;

    public static BriefingUrgency TaskUrgency(DateOnly due, DateOnly today) =>
        due < today ? BriefingUrgency.Overdue : due == today ? BriefingUrgency.Today : BriefingUrgency.Soon;

    /// <summary>Most urgent first; within a tier, the one that is latest by the most; then biometrics, PAL, tasks; then by title.</summary>
    public static List<BriefingItem> Rank(IEnumerable<BriefingItem> items) =>
        items.OrderBy(i => i.Urgency).ThenByDescending(i => i.DaysLate).ThenBy(i => KindOrder(i.Kind)).ThenBy(i => i.Title, StringComparer.OrdinalIgnoreCase).ToList();

    public static string Headline(int overdue, int today, int soon)
    {
        var total = overdue + today + soon;
        if (total == 0) return "Nothing needs your attention today.";
        var parts = new List<string>();
        if (overdue > 0) parts.Add($"{overdue} overdue");
        if (today > 0) parts.Add($"{today} due today");
        if (soon > 0) parts.Add($"{soon} coming up");
        return $"{(total == 1 ? "1 thing needs" : $"{total} things need")} your attention: {string.Join(", ", parts)}.";
    }

    private static int KindOrder(string kind) => kind switch { Biometrics => 0, Pal => 1, Document => 2, _ => 3 };
}

/// <summary>Today's briefing for the signed-in staff member: their own open tasks, plus the case alerts everyone shares.</summary>
public record GetBriefingQuery : IQuery<BriefingResult>;

public class GetBriefingHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<GetBriefingQuery, BriefingResult>
{
    public async ValueTask<BriefingResult> Handle(GetBriefingQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.CaseWorkers);
        var today = AdmissionMapping.Today(clock);
        var items = new List<BriefingItem>();

        // Shared: biometrics that haven't been marked done. Overdue once the wait is long or the appointment has passed; an appointment today or tomorrow is flagged ahead of time.
        foreach (var f in await BiometricsFollowUps.ListAsync(db, today, cancellationToken))
        {
            var appointment = f.AppointmentOn;
            var urgency = f.Overdue ? BriefingUrgency.Overdue
                : appointment == today ? BriefingUrgency.Today
                : appointment == today.AddDays(1) ? BriefingUrgency.Soon
                : (BriefingUrgency?)null;
            if (urgency is null) continue;
            var lateByWait = f.DaysWaiting - VisaRules.BiometricsFollowUpAfterDays;
            var lateByAppointment = appointment is { } a && a < today ? today.DayNumber - a.DayNumber : 0;
            var detail = appointment is { } on && on >= today
                ? $"{f.InstitutionName} · appointment {(on == today ? "today" : "tomorrow")}, not marked done"
                : lateByAppointment > 0
                    ? $"{f.InstitutionName} · appointment was {lateByAppointment} day{(lateByAppointment == 1 ? "" : "s")} ago, not marked done"
                    : $"{f.InstitutionName} · {f.DaysWaiting} days since submitted, not marked done";
            items.Add(new BriefingItem(BriefingRules.Biometrics, urgency.Value, $"Biometrics: {f.CandidateName}", detail, $"/staff/visa/{f.VisaCaseId}", Math.Max(0, Math.Max(lateByWait, lateByAppointment))));
        }

        // Shared: acceptance received but no PAL yet, once past the follow-up limit.
        foreach (var f in await PalFollowUps.ListAsync(db, clock.UtcNow, cancellationToken))
        {
            if (!f.Overdue) continue;
            items.Add(new BriefingItem(BriefingRules.Pal, BriefingUrgency.Overdue, $"PAL letter: {f.CandidateName}", $"{f.InstitutionName} · {f.DaysWaiting} days since the acceptance, no PAL yet", $"/staff/admissions/{f.ApplicationId}", Math.Max(0, f.DaysWaiting - PalRules.FollowUpAfterDays)));
        }

        // Shared: documents staff sent back that the student has now sent again. Someone is waiting for a decision.
        var visaOnly = await Slots.VisaOnlyTypeIdsAsync(db, cancellationToken);
        var sentBack = await db.DocumentReviews.AsNoTracking().Where(r => r.Decision == DocumentStatus.NeedsResubmission).Select(r => r.DocumentId).Distinct().ToListAsync(cancellationToken);
        var resubmitted = await db.CandidateDocuments.AsNoTracking().Where(d => d.Status == DocumentStatus.Uploaded && sentBack.Contains(d.Id) && !visaOnly.Contains(d.DocumentTypeId)).ToListAsync(cancellationToken);
        if (resubmitted.Count > 0)
        {
            var ids = resubmitted.Select(d => d.Id).ToList();
            var lastReview = (await db.DocumentReviews.AsNoTracking().Where(r => ids.Contains(r.DocumentId)).ToListAsync(cancellationToken)).GroupBy(r => r.DocumentId).ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.ReviewedAtUtc).First());
            var sentAgain = (await db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId) && v.RemovedAtUtc == null).Select(v => new { v.DocumentId, v.UploadedAtUtc }).ToListAsync(cancellationToken)).GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => g.Max(v => v.UploadedAtUtc));
            var typeNames = await db.DocumentTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, cancellationToken);
            var owners = resubmitted.Select(d => d.CandidateId).Distinct().ToList();
            var people = await db.CandidateProfiles.AsNoTracking().Where(c => owners.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken);
            foreach (var d in resubmitted)
            {
                // Only if the last decision was "send it back" and something new has come in since.
                if (lastReview.GetValueOrDefault(d.Id) is not { Decision: DocumentStatus.NeedsResubmission } review || !sentAgain.TryGetValue(d.Id, out var at) || at <= review.ReviewedAtUtc) continue;
                var waiting = Math.Max(0, today.DayNumber - DateOnly.FromDateTime(at).DayNumber);
                var lateForDecision = waiting >= BriefingRules.ResubmittedOverdueAfterDays;
                var detail = $"{typeNames.GetValueOrDefault(d.DocumentTypeId, "Document")} sent again {(waiting == 0 ? "today" : waiting == 1 ? "yesterday" : $"{waiting} days ago")}, waiting for a decision";
                items.Add(new BriefingItem(BriefingRules.Document, lateForDecision ? BriefingUrgency.Overdue : BriefingUrgency.Today, $"Sent again: {people.GetValueOrDefault(d.CandidateId, "A student")}", detail, $"/staff/candidates/{d.CandidateId}", lateForDecision ? waiting - BriefingRules.ResubmittedOverdueAfterDays : 0));
            }
        }

        // Mine only: open tasks that are overdue, due today, or due in the next few days.
        var horizon = today.AddDays(BriefingRules.TaskSoonDays);
        var tasks = await db.StaffTasks.AsNoTracking()
            .Where(t => t.AssigneeUserId == userId && t.Status == StaffTaskStatus.Open && t.DueOn != null && t.DueOn <= horizon)
            .Select(t => new { t.Id, t.Title, t.DueOn, t.CandidateId })
            .ToListAsync(cancellationToken);
        var candidateIds = tasks.Where(t => t.CandidateId != null).Select(t => t.CandidateId!.Value).Distinct().ToList();
        var names = await db.CandidateProfiles.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken);
        foreach (var t in tasks)
        {
            var due = t.DueOn!.Value;
            var urgency = BriefingRules.TaskUrgency(due, today);
            var late = Math.Max(0, today.DayNumber - due.DayNumber);
            var when = urgency == BriefingUrgency.Overdue ? $"overdue by {late} day{(late == 1 ? "" : "s")}" : urgency == BriefingUrgency.Today ? "due today" : $"due {due:d MMM}";
            var who = t.CandidateId is { } c && names.TryGetValue(c, out var n) ? $"{n} · " : "";
            items.Add(new BriefingItem(BriefingRules.Task, urgency, t.Title, who + when, t.CandidateId is { } id ? $"/staff/candidates/{id}" : "/staff/tasks", late));
        }

        var ranked = BriefingRules.Rank(items);
        int Count(BriefingUrgency u) => ranked.Count(i => i.Urgency == u);
        var (overdue, due_today, soon) = (Count(BriefingUrgency.Overdue), Count(BriefingUrgency.Today), Count(BriefingUrgency.Soon));
        return new BriefingResult(BriefingRules.Headline(overdue, due_today, soon), overdue, due_today, soon, ranked.Take(BriefingRules.MaxItems).ToList());
    }
}
