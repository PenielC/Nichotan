using Nichotan.Domain.Applications;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Admissions;

// ─────────────────────────── The directory ───────────────────────────

/// <summary>A university or college. Archived, never deleted, so existing applications keep their reference.</summary>
public class Institution : Entity
{
    private Institution() { }

    public Institution(string name, DestinationCountry country, string? website, string? notes)
    {
        Country = country;
        Update(name, website, notes);
    }

    public string Name { get; private set; } = string.Empty;
    public DestinationCountry Country { get; private set; }
    public string? Website { get; private set; }
    public string? Notes { get; private set; }
    public bool IsArchived { get; private set; }

    public void Update(string name, string? website, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("An institution needs a name.");
        Name = name.Trim();
        Website = string.IsNullOrWhiteSpace(website) ? null : website.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    public void SetArchived(bool archived) => IsArchived = archived;
}

/// <summary>A term and year in which a programme starts.</summary>
public class ProgrammeIntake
{
    public ProgrammeIntake(StartTerm term, int year)
    {
        Term = term;
        Year = year;
    }

    public StartTerm Term { get; private set; }
    public int Year { get; private set; }
}

/// <summary>A programme an institution offers, at one applicant level, with the intakes it starts in.</summary>
public class Programme : Entity
{
    private readonly List<ProgrammeIntake> _intakes = [];

    private Programme() { }

    public Programme(Guid institutionId, string name, ApplicantLevel level, IEnumerable<ProgrammeIntake> intakes)
    {
        InstitutionId = institutionId;
        Update(name, level, intakes);
    }

    public Guid InstitutionId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ApplicantLevel Level { get; private set; }
    public bool IsArchived { get; private set; }
    public IReadOnlyList<ProgrammeIntake> Intakes => _intakes;

    public bool Offers(StartTerm term, int year) => _intakes.Any(i => i.Term == term && i.Year == year);

    public void Update(string name, ApplicantLevel level, IEnumerable<ProgrammeIntake> intakes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("A programme needs a name.");
        Name = name.Trim();
        Level = level;
        _intakes.Clear();
        _intakes.AddRange(intakes.DistinctBy(i => (i.Term, i.Year)).OrderBy(i => i.Year).ThenBy(i => i.Term));
    }

    public void SetArchived(bool archived) => IsArchived = archived;
}

// ─────────────────────────── The workflow ───────────────────────────

/// <summary>
/// Where an application stands. The school's own portal is outside our control, so every status is recorded
/// by staff from what they did or were told there; nothing here is synced with an institution.
/// </summary>
public enum AdmissionStatus
{
    Draft,
    DocumentsRequired,
    ReadyForReview,
    SubmittedToInstitution,
    AwaitingDecision,
    OfferReceived,
    Rejected,
    MoreInformationRequired,
    ResubmissionRequired,
    Withdrawn,
    Closed,
}

public static class AdmissionWorkflow
{
    private static readonly IReadOnlyDictionary<AdmissionStatus, AdmissionStatus[]> Moves = new Dictionary<AdmissionStatus, AdmissionStatus[]>
    {
        [AdmissionStatus.Draft] = [AdmissionStatus.DocumentsRequired, AdmissionStatus.ReadyForReview, AdmissionStatus.Withdrawn],
        [AdmissionStatus.DocumentsRequired] = [AdmissionStatus.ReadyForReview, AdmissionStatus.Withdrawn],
        [AdmissionStatus.ReadyForReview] = [AdmissionStatus.SubmittedToInstitution, AdmissionStatus.DocumentsRequired, AdmissionStatus.Withdrawn],
        // The school can decide before anyone records "awaiting decision", so a decision may follow a submission directly.
        [AdmissionStatus.SubmittedToInstitution] =
        [
            AdmissionStatus.AwaitingDecision, AdmissionStatus.OfferReceived, AdmissionStatus.Rejected,
            AdmissionStatus.MoreInformationRequired, AdmissionStatus.ResubmissionRequired, AdmissionStatus.Withdrawn,
        ],
        [AdmissionStatus.AwaitingDecision] =
        [
            AdmissionStatus.OfferReceived, AdmissionStatus.Rejected, AdmissionStatus.MoreInformationRequired,
            AdmissionStatus.ResubmissionRequired, AdmissionStatus.Withdrawn,
        ],
        [AdmissionStatus.MoreInformationRequired] = [AdmissionStatus.SubmittedToInstitution, AdmissionStatus.Withdrawn],
        [AdmissionStatus.ResubmissionRequired] = [AdmissionStatus.ReadyForReview, AdmissionStatus.SubmittedToInstitution, AdmissionStatus.Withdrawn],
        // The offer, deposit and formal acceptance are separate records (a later step); the application stays here meanwhile.
        [AdmissionStatus.OfferReceived] = [AdmissionStatus.Closed, AdmissionStatus.Withdrawn],
        [AdmissionStatus.Rejected] = [AdmissionStatus.Closed],
        [AdmissionStatus.Withdrawn] = [],
        [AdmissionStatus.Closed] = [],
    };

    private static readonly HashSet<AdmissionStatus> NoteRequiredFor =
    [
        AdmissionStatus.Rejected, AdmissionStatus.Withdrawn, AdmissionStatus.MoreInformationRequired, AdmissionStatus.ResubmissionRequired,
    ];

    public static IReadOnlyList<AdmissionStatus> NextFrom(AdmissionStatus from) => Moves[from];

    public static bool CanMove(AdmissionStatus from, AdmissionStatus to) => Moves[from].Contains(to);

    public static bool RequiresNote(AdmissionStatus to) => NoteRequiredFor.Contains(to);

    /// <summary>
    /// The school may reply to the student directly, so once staff have applied on the student's behalf the student can
    /// upload the offer letter without waiting for staff to record it.
    /// </summary>
    public static bool AcceptsOfferLetter(AdmissionStatus status) =>
        status is AdmissionStatus.SubmittedToInstitution or AdmissionStatus.AwaitingDecision or AdmissionStatus.OfferReceived
            or AdmissionStatus.MoreInformationRequired or AdmissionStatus.ResubmissionRequired;

    /// <summary>Finished: no further moves, and no longer counts as an active application.</summary>
    public static bool IsActive(AdmissionStatus status) => status is not (AdmissionStatus.Rejected or AdmissionStatus.Withdrawn or AdmissionStatus.Closed);
}

// ─────────────────────────── The application ───────────────────────────

/// <summary>One candidate's application to one programme and intake. A candidate can have several.</summary>
public class AdmissionApplication : Entity
{
    private AdmissionApplication() { }

    public AdmissionApplication(Guid candidateId, Institution institution, Programme programme, StartTerm term, int year, DateTime nowUtc)
    {
        if (institution.IsArchived || programme.IsArchived) throw new DomainException("This programme is archived and can't take new applications.");
        if (programme.InstitutionId != institution.Id) throw new DomainException("The programme doesn't belong to that institution.");
        if (!programme.Offers(term, year)) throw new DomainException("This programme doesn't list that intake.");
        CandidateId = candidateId;
        InstitutionId = institution.Id;
        ProgrammeId = programme.Id;
        Country = institution.Country;
        Level = programme.Level;
        IntakeTerm = term;
        IntakeYear = year;
        Status = AdmissionStatus.Draft;
        StatusChangedAtUtc = nowUtc;
    }

    public Guid CandidateId { get; private set; }
    public Guid InstitutionId { get; private set; }
    public Guid ProgrammeId { get; private set; }

    /// <summary>Copied from the institution and programme when the application is created, so later directory edits don't rewrite it.</summary>
    public DestinationCountry Country { get; private set; }
    public ApplicantLevel Level { get; private set; }

    public StartTerm IntakeTerm { get; private set; }
    public int IntakeYear { get; private set; }

    public AdmissionStatus Status { get; private set; }
    public DateTime StatusChangedAtUtc { get; private set; }

    /// <summary>The school's own application or reference number, from its portal.</summary>
    public string? InstitutionReference { get; private set; }

    /// <summary>A link to this application on the school's portal. No logins are stored.</summary>
    public string? PortalUrl { get; private set; }

    /// <summary>The day staff submitted (or last resubmitted) it on the school's portal.</summary>
    public DateOnly? SubmittedToInstitutionOn { get; private set; }

    public void UpdateDetails(string? institutionReference, string? portalUrl)
    {
        if (!AdmissionWorkflow.IsActive(Status)) throw new DomainException("This application is finished and can't be edited.");
        InstitutionReference = string.IsNullOrWhiteSpace(institutionReference) ? null : institutionReference.Trim();
        PortalUrl = string.IsNullOrWhiteSpace(portalUrl) ? null : portalUrl.Trim();
    }

    /// <summary>The first record in an application's history.</summary>
    public AdmissionStatusEvent RecordCreated(DateOnly today, Guid userId, string? userEmail, DateTime nowUtc) =>
        new(Id, null, Status, today, nowUtc, userId, userEmail, "Application created.", isCorrection: false);

    /// <summary>Moves the application along the allowed path and returns the history record for the move.</summary>
    public AdmissionStatusEvent MoveTo(AdmissionStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc)
    {
        if (!AdmissionWorkflow.CanMove(Status, to)) throw new DomainException($"An application that is {Describe(Status)} can't move to {Describe(to)}.");
        if (AdmissionWorkflow.RequiresNote(to) && string.IsNullOrWhiteSpace(note)) throw new DomainException($"Add a note explaining why this is {Describe(to)}.");
        return Apply(to, occurredOn, note, userId, userEmail, today, nowUtc, isCorrection: false);
    }

    /// <summary>Fixes a status that was recorded wrongly. The earlier history stays; the correction is added after it.</summary>
    public AdmissionStatusEvent Correct(AdmissionStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc)
    {
        if (to == Status) throw new DomainException("The application already has that status.");
        if (string.IsNullOrWhiteSpace(note)) throw new DomainException("Explain what is being corrected.");
        return Apply(to, occurredOn, note, userId, userEmail, today, nowUtc, isCorrection: true);
    }

    private AdmissionStatusEvent Apply(AdmissionStatus to, DateOnly occurredOn, string? note, Guid userId, string? userEmail, DateOnly today, DateTime nowUtc, bool isCorrection)
    {
        if (occurredOn > today) throw new DomainException("The date can't be in the future.");
        var from = Status;
        Status = to;
        StatusChangedAtUtc = nowUtc;
        if (to == AdmissionStatus.SubmittedToInstitution) SubmittedToInstitutionOn = occurredOn;
        return new AdmissionStatusEvent(Id, from, to, occurredOn, nowUtc, userId, userEmail, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), isCorrection);
    }

    public static string Describe(AdmissionStatus status) => status switch
    {
        AdmissionStatus.DocumentsRequired => "waiting for documents",
        AdmissionStatus.ReadyForReview => "ready for review",
        AdmissionStatus.SubmittedToInstitution => "submitted to the institution",
        AdmissionStatus.AwaitingDecision => "awaiting a decision",
        AdmissionStatus.OfferReceived => "an offer received",
        AdmissionStatus.MoreInformationRequired => "waiting on more information",
        AdmissionStatus.ResubmissionRequired => "needing resubmission",
        _ => status.ToString().ToLowerInvariant(),
    };
}

/// <summary>
/// One entry in an application's history. Never edited or deleted. OccurredOn is when it happened at the
/// school (or in the office); RecordedAtUtc is when staff entered it. Reports use OccurredOn.
/// </summary>
public class AdmissionStatusEvent : Entity
{
    private AdmissionStatusEvent() { }

    internal AdmissionStatusEvent(Guid applicationId, AdmissionStatus? from, AdmissionStatus to, DateOnly occurredOn, DateTime recordedAtUtc, Guid recordedByUserId, string? recordedByEmail, string? note, bool isCorrection)
    {
        ApplicationId = applicationId;
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

    public Guid ApplicationId { get; private set; }
    public AdmissionStatus? From { get; private set; }
    public AdmissionStatus To { get; private set; }
    public DateOnly OccurredOn { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public Guid RecordedByUserId { get; private set; }
    public string? RecordedByEmail { get; private set; }
    public string? Note { get; private set; }
    public bool IsCorrection { get; private set; }
}

public enum SuggestionStatus { New, Added, Declined }

/// <summary>A school and programme a student wants to apply to that isn't in the directory yet. Staff add it (or decline) and tell the student.</summary>
public class SchoolSuggestion : Entity
{
    private SchoolSuggestion() { }

    public SchoolSuggestion(Guid candidateId, string institutionName, DestinationCountry country, string programmeName, StartTerm? intakeTerm, int? intakeYear, string? note)
    {
        if (string.IsNullOrWhiteSpace(institutionName)) throw new DomainException("Tell us the name of the school.");
        if (string.IsNullOrWhiteSpace(programmeName)) throw new DomainException("Tell us which programme you'd like.");
        CandidateId = candidateId;
        InstitutionName = institutionName.Trim();
        Country = country;
        ProgrammeName = programmeName.Trim();
        IntakeTerm = intakeTerm;
        IntakeYear = intakeYear;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Status = SuggestionStatus.New;
    }

    public Guid CandidateId { get; private set; }
    public string InstitutionName { get; private set; } = string.Empty;
    public DestinationCountry Country { get; private set; }
    public string ProgrammeName { get; private set; } = string.Empty;
    public StartTerm? IntakeTerm { get; private set; }
    public int? IntakeYear { get; private set; }
    public string? Note { get; private set; }
    public SuggestionStatus Status { get; private set; }
    public string? StaffNote { get; private set; }
    public string? HandledByEmail { get; private set; }
    public DateTime? HandledAtUtc { get; private set; }

    public void Update(SuggestionStatus status, string? staffNote, string? staffEmail, DateTime nowUtc)
    {
        Status = status;
        StaffNote = string.IsNullOrWhiteSpace(staffNote) ? null : staffNote.Trim();
        HandledByEmail = staffEmail;
        HandledAtUtc = nowUtc;
    }
}
