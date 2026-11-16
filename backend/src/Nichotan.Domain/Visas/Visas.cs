using Nichotan.Domain.Common;

namespace Nichotan.Domain.Visas;

/// <summary>
/// Where a visa case stands. The immigration authority's own system is outside our control, so every status is recorded by
/// staff from what they did or were told; nothing here is synced with an authority.
/// </summary>
public enum VisaStatus
{
    Preparing,
    Submitted,
    BiometricsRequested,
    BiometricsCompleted,
    AdditionalInformationRequested,
    Approved,
    Refused,
    Withdrawn,
}

public static class VisaRules
{
    /// <summary>Days after a visa application is submitted with biometrics still not done before staff are told it's overdue.</summary>
    public const int BiometricsFollowUpAfterDays = 5;
}

public static class VisaWorkflow
{
    private static readonly IReadOnlyDictionary<VisaStatus, VisaStatus[]> Moves = new Dictionary<VisaStatus, VisaStatus[]>
    {
        [VisaStatus.Preparing] = [VisaStatus.Submitted, VisaStatus.Withdrawn],
        // A decision, a biometrics request or a request for more information can each follow a submission directly.
        // Biometrics can be done without anyone recording a separate "requested" step, so it may follow a submission directly.
        [VisaStatus.Submitted] = [VisaStatus.BiometricsRequested, VisaStatus.BiometricsCompleted, VisaStatus.AdditionalInformationRequested, VisaStatus.Approved, VisaStatus.Refused, VisaStatus.Withdrawn],
        [VisaStatus.BiometricsRequested] = [VisaStatus.BiometricsCompleted, VisaStatus.Withdrawn],
        [VisaStatus.BiometricsCompleted] = [VisaStatus.AdditionalInformationRequested, VisaStatus.Approved, VisaStatus.Refused, VisaStatus.Withdrawn],
        // Once the information has been sent back, the case is simply under consideration again.
        [VisaStatus.AdditionalInformationRequested] = [VisaStatus.Submitted, VisaStatus.Withdrawn],
        [VisaStatus.Approved] = [],
        [VisaStatus.Refused] = [],
        [VisaStatus.Withdrawn] = [],
    };

    private static readonly HashSet<VisaStatus> NoteRequiredFor = [VisaStatus.AdditionalInformationRequested, VisaStatus.Refused, VisaStatus.Withdrawn];

    public static IReadOnlyList<VisaStatus> NextFrom(VisaStatus from) => Moves[from];

    public static bool CanMove(VisaStatus from, VisaStatus to) => Moves[from].Contains(to);

    public static bool RequiresNote(VisaStatus to) => NoteRequiredFor.Contains(to);

    /// <summary>Lodged, and biometrics not yet done: this is when staff are reminded and the student can say they've been done.</summary>
    public static bool AwaitingBiometrics(VisaStatus status) => status is VisaStatus.Submitted or VisaStatus.BiometricsRequested;

    /// <summary>The authority's biometrics letter can be added once the case has been lodged, until it is decided.</summary>
    public static bool AcceptsBiometricsLetter(VisaStatus status) => status is VisaStatus.Submitted or VisaStatus.BiometricsRequested or VisaStatus.BiometricsCompleted or VisaStatus.AdditionalInformationRequested;

    /// <summary>Finished: no further moves.</summary>
    public static bool IsFinished(VisaStatus status) => status is VisaStatus.Approved or VisaStatus.Refused or VisaStatus.Withdrawn;

    /// <summary>"In process" means lodged with the authority and not yet decided; a case still being prepared isn't counted.</summary>
    public static bool InProcess(VisaStatus status) => status is VisaStatus.Submitted or VisaStatus.BiometricsRequested or VisaStatus.BiometricsCompleted or VisaStatus.AdditionalInformationRequested;
}

/// <summary>
/// One candidate's visa application, for one accepted admission application. The admission is the link to the country,
/// school and programme; the case never stands alone.
/// </summary>
public class VisaCase : Entity
{
    private VisaCase() { }

    public VisaCase(Guid applicationId, Guid candidateId, DateOnly today, DateTime nowUtc)
    {
        ApplicationId = applicationId;
        CandidateId = candidateId;
        Status = VisaStatus.Preparing;
        StatusChangedAtUtc = nowUtc;
        OpenedOn = today;
    }

    public Guid ApplicationId { get; private set; }
    public Guid CandidateId { get; private set; }
    public VisaStatus Status { get; private set; }
    public DateTime StatusChangedAtUtc { get; private set; }
    public DateOnly OpenedOn { get; private set; }

    /// <summary>The authority's own file or application number.</summary>
    public string? ReferenceNumber { get; private set; }

    public DateOnly? SubmittedOn { get; private set; }

    /// <summary>The biometrics appointment, once the authority has given one.</summary>
    public DateOnly? BiometricsAppointmentOn { get; private set; }

    public string? BiometricsLocation { get; private set; }

    public DateOnly? DecisionOn { get; private set; }

    public void UpdateDetails(string? referenceNumber, DateOnly? biometricsAppointmentOn, string? biometricsLocation)
    {
        if (VisaWorkflow.IsFinished(Status)) throw new DomainException("This visa case is finished and can't be edited.");
        ReferenceNumber = string.IsNullOrWhiteSpace(referenceNumber) ? null : referenceNumber.Trim();
        BiometricsAppointmentOn = biometricsAppointmentOn;
        BiometricsLocation = string.IsNullOrWhiteSpace(biometricsLocation) ? null : biometricsLocation.Trim();
    }

    public VisaStatusEvent RecordOpened(Guid userId, string? userEmail, DateTime nowUtc) =>
        new(Id, null, Status, OpenedOn, nowUtc, userId, userEmail, "Visa case opened.", isCorrection: false);

    public VisaStatusEvent MoveTo(VisaStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc)
    {
        if (!VisaWorkflow.CanMove(Status, to)) throw new DomainException($"A visa case that is {Describe(Status)} can't move to {Describe(to)}.");
        if (VisaWorkflow.RequiresNote(to) && string.IsNullOrWhiteSpace(note)) throw new DomainException($"Add a note explaining why this is {Describe(to)}.");
        return Apply(to, occurredOn, note, userId, userEmail, today, nowUtc, isCorrection: false);
    }

    /// <summary>Fixes a status that was recorded wrongly. The earlier history stays; the correction is added after it.</summary>
    public VisaStatusEvent Correct(VisaStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc)
    {
        if (to == Status) throw new DomainException("The visa case already has that status.");
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Explain what is being corrected.");
        return Apply(to, occurredOn, note, userId, userEmail, today, nowUtc, isCorrection: true);
    }

    private VisaStatusEvent Apply(VisaStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc, bool isCorrection)
    {
        if (occurredOn > today) throw new DomainException("The date can't be in the future.");
        var from = Status;
        Status = to;
        StatusChangedAtUtc = nowUtc;
        if (to == VisaStatus.Submitted && from != VisaStatus.AdditionalInformationRequested) SubmittedOn = occurredOn;
        DecisionOn = to is VisaStatus.Approved or VisaStatus.Refused ? occurredOn : null;
        return new VisaStatusEvent(Id, from, to, occurredOn, nowUtc, userId, userEmail, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), isCorrection);
    }

    public static string Describe(VisaStatus status) => status switch
    {
        VisaStatus.BiometricsRequested => "waiting for biometrics",
        VisaStatus.BiometricsCompleted => "biometrics completed",
        VisaStatus.AdditionalInformationRequested => "waiting on more information",
        _ => status.ToString().ToLowerInvariant(),
    };
}

/// <summary>One entry in a visa case's history. Never edited or deleted. OccurredOn is when it happened; RecordedAtUtc is when staff entered it.</summary>
public class VisaStatusEvent : Entity
{
    private VisaStatusEvent() { }

    internal VisaStatusEvent(Guid visaCaseId, VisaStatus? from, VisaStatus to, DateOnly occurredOn, DateTime recordedAtUtc, Guid recordedByUserId, string? recordedByEmail, string? note, bool isCorrection)
    {
        VisaCaseId = visaCaseId;
        From = from;
        To = to;
        OccurredOn = occurredOn;
        RecordedAtUtc = recordedAtUtc;
        RecordedByUserId = recordedByUserId;
        RecordedByEmail = recordedByEmail;
        Note = note;
        IsCorrection = isCorrection;
        CreatedAtUtc = recordedAtUtc;
    }

    public Guid VisaCaseId { get; private set; }
    public VisaStatus? From { get; private set; }
    public VisaStatus To { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public string? RecordedByEmail { get; private set; }
    public string? Note { get; private set; }
    public bool IsCorrection { get; private set; }
}
