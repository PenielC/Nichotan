namespace Nichotan.Domain.Reporting;

/// <summary>One recorded move of something (an admission application or a visa case) into a status.</summary>
/// <param name="OccurredOn">The day it happened, as staff recorded it. Reports use this, not the day it was typed in.</param>
public readonly record struct Transition(Guid SubjectId, string Status, DateOnly OccurredOn, DateTime RecordedAtUtc, bool IsCorrection);

/// <summary>
/// The two questions every report asks, kept apart on purpose:
/// <list type="bullet">
/// <item><b>Current pipeline</b>: what status is each record in as at a day? (<see cref="StatusAsAt"/>)</item>
/// <item><b>Movement</b>: which records moved into a status during a period? (<see cref="MovedInto"/>)</item>
/// </list>
/// Both come from the recorded transitions, so a past month can be reported long after the records moved on.
/// </summary>
public static class ReportMath
{
    /// <summary>
    /// The status each record was in at the end of <paramref name="asAt"/>. A move belongs to the day it happened
    /// (<c>OccurredOn</c>), but the <i>order</i> of moves is the order they were recorded in: a record is added to the system
    /// today even if it started elsewhere long ago, so staff may enter earlier-dated moves after it. Among the moves dated on or
    /// before the day, the one recorded last is the status. Records with no move by then didn't exist yet and are left out.
    /// Corrections count here, because they say what the status really was.
    /// </summary>
    public static Dictionary<Guid, string> StatusAsAt(IEnumerable<Transition> transitions, DateOnly asAt) =>
        transitions.Where(t => t.OccurredOn <= asAt)
            .GroupBy(t => t.SubjectId)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.RecordedAtUtc).Last().Status);

    /// <summary>
    /// Records that moved into <paramref name="status"/> on a day within the period (both ends included), each counted once however
    /// many times it moved there. Corrections are not movement: they fix an earlier entry rather than record something happening.
    /// </summary>
    public static HashSet<Guid> MovedInto(IEnumerable<Transition> transitions, string status, DateOnly from, DateOnly to) =>
        transitions.Where(t => !t.IsCorrection && t.Status == status && t.OccurredOn >= from && t.OccurredOn <= to).Select(t => t.SubjectId).ToHashSet();

    /// <summary>Records that had moved into <paramref name="status"/> at any point up to and including <paramref name="asAt"/>.</summary>
    public static HashSet<Guid> EverReached(IEnumerable<Transition> transitions, string status, DateOnly asAt) =>
        transitions.Where(t => !t.IsCorrection && t.Status == status && t.OccurredOn <= asAt).Select(t => t.SubjectId).ToHashSet();
}
