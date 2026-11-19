namespace Nichotan.Domain.Audit;

public enum AuditOutcome
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}

/// <summary>Security-relevant and material actions. Stored by name, so new values can be added safely.</summary>
public enum AuditAction
{
    CandidateRegistered,
    LoginSucceeded,
    LoginFailed,
    LoginLockedOut,
    MfaChallengePassed,
    MfaChallengeFailed,
    LoggedOut,
    RefreshTokenReuseDetected,
    EmailVerified,
    PasswordResetRequested,
    PasswordReset,
    MfaEnabled,
    MfaDisabled,
    RecoveryCodesRegenerated,
    StaffInvited,
    StaffInvitationAccepted,
    StaffRolesChanged,
    StaffDisabled,
    StaffEnabled,
    CandidateProfileUpdated,
    CandidateProfileViewed,
    AccessDenied,
    ApplicationFormSubmitted,
    ApplicationFormReopened,
    ApplicationFormViewed,
    DirectoryChanged,
    AdmissionApplicationCreated,
    AdmissionStatusChanged,
    AdmissionStatusCorrected,
    AdmissionDetailsUpdated,
    DocumentUploaded,
    DocumentFileRemoved,
    DocumentReviewed,
    DocumentDownloaded,
    ChecklistChanged,
    DocumentRulesChanged,
    EnquiryUpdated,
    EnquiryViewed,
    SchoolSuggested,
    SchoolSuggestionUpdated,
    OfferRecorded,
    // The deposit step was removed; these stay so audit rows written earlier can still be read.
    DepositUpdated,
    DepositVerified,
    DepositRejected,
    AcceptanceRecorded,
    VisaCaseOpened,
    VisaStatusChanged,
    VisaStatusCorrected,
    VisaDetailsUpdated,
    PreDepartureActivated,
    PreDepartureItemChanged,
    PreDepartureTemplateChanged,
    TaskCreated,
    TaskUpdated,
    CandidateTimelineViewed,
    WhatsAppShareOpened,
}

/// <summary>
/// One audit record: who did what to which record, when, from where, and whether it worked. Details hold
/// small, non-sensitive context (never passwords, tokens, document contents, passport or financial data).
/// Audit rows are only ever appended.
/// </summary>
public class AuditEvent
{
    private AuditEvent() { }

    public AuditEvent(
        DateTime occurredAtUtc,
        AuditAction action,
        AuditOutcome outcome,
        Guid? actorUserId,
        string? actorEmail,
        string? entityType,
        string? entityId,
        string? ipAddress,
        string? details)
    {
        OccurredAtUtc = occurredAtUtc;
        Action = action;
        Outcome = outcome;
        ActorUserId = actorUserId;
        ActorEmail = actorEmail;
        EntityType = entityType;
        EntityId = entityId;
        IpAddress = ipAddress;
        Details = details;
    }

    public long Id { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public AuditAction Action { get; private set; }
    public AuditOutcome Outcome { get; private set; }
    public Guid? ActorUserId { get; private set; }

    /// <summary>A snapshot, so the entry still reads well if the account is later changed.</summary>
    public string? ActorEmail { get; private set; }

    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>Short JSON with non-sensitive context, e.g. {"roles":{"from":[...],"to":[...]}}.</summary>
    public string? Details { get; private set; }
}
