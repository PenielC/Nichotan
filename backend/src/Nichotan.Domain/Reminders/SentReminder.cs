using Nichotan.Domain.Common;

namespace Nichotan.Domain.Reminders;

/// <summary>
/// A record that one reminder has been sent. The pair (<see cref="Kind"/>, <see cref="SubjectKey"/>) is unique, so claiming it
/// before sending is what stops the same reminder going out twice, however often the worker runs or restarts.
/// </summary>
public class SentReminder : Entity
{
    private SentReminder() { }

    public SentReminder(string kind, string subjectKey, DateTime sentAtUtc)
    {
        Kind = kind;
        SubjectKey = subjectKey;
        SentAtUtc = sentAtUtc;
    }

    /// <summary>What sort of reminder this is, such as "BiometricsStudent".</summary>
    public string Kind { get; private set; } = string.Empty;

    /// <summary>Which record and which round it was for, such as "{visaCaseId}:1". Never contains personal details.</summary>
    public string SubjectKey { get; private set; } = string.Empty;

    public DateTime SentAtUtc { get; private set; }
}
