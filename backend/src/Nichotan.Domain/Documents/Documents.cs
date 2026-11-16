using Nichotan.Domain.Applications;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Documents;

/// <summary>Whether one upload serves every application (Candidate) or belongs to a single application.</summary>
public enum DocumentScope { Candidate, Application }

/// <summary>
/// Where an uploaded document stands. "Missing" isn't stored: it just means nothing has been uploaded.
/// Uploading and reviewing are separate: an upload alone is never "verified".
/// </summary>
/// <summary>
/// What a document type is for. Anything other than None is managed by the system (the school's
/// letters, the visa biometrics letter and decision letter): it isn't a checklist item.
/// </summary>
public enum DocumentPurpose { None, OfferLetter, AcceptanceLetter, PalLetter, BiometricsLetter, DecisionLetter }

/// <summary>Which part of the journey a checklist is for: getting into the school, or getting the visa.</summary>
public enum ChecklistStage { Admission, Visa }

public enum DocumentStatus { Uploaded, UnderReview, Accepted, NeedsResubmission }

/// <summary>A kind of document Nichotan asks for, such as a passport or a Statement of Purpose.</summary>
public class DocumentType : Entity
{
    private DocumentType() { }

    public DocumentType(string name, string? description, DocumentScope scope, bool tracksExpiry, bool isAutomatic, int sortOrder, DocumentPurpose purpose = DocumentPurpose.None)
    {
        Purpose = purpose;
        Scope = scope;
        IsAutomatic = isAutomatic;
        SortOrder = sortOrder;
        IsActive = true;
        Update(name, description, tracksExpiry);
    }

    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DocumentScope Scope { get; private set; }

    /// <summary>Documents of this kind can carry an expiry date (a passport).</summary>
    public bool TracksExpiry { get; private set; }

    /// <summary>Met by something the system already knows (the signed Nichotan form), so nothing is uploaded.</summary>
    public bool IsAutomatic { get; private set; }

    public DocumentPurpose Purpose { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, string? description, bool tracksExpiry)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("A document type needs a name.");
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        TracksExpiry = tracksExpiry;
    }

    public void SetActive(bool active) => IsActive = active;
}

/// <summary>
/// One line of the checklist template: "documents of this type are (or are not) required for applications that
/// match the country, level and institution given". A blank field matches anything. When several lines match,
/// the most specific wins (institution, then country, then level); if still tied, "required" wins.
/// </summary>
public class DocumentRequirement : Entity
{
    private DocumentRequirement() { }

    public DocumentRequirement(Guid documentTypeId, DestinationCountry? country, ApplicantLevel? level, Guid? institutionId, bool isRequired, ChecklistStage stage = ChecklistStage.Admission)
    {
        Stage = stage;
        DocumentTypeId = documentTypeId;
        Country = country;
        Level = level;
        InstitutionId = institutionId;
        IsRequired = isRequired;
        IsActive = true;
    }

    public Guid DocumentTypeId { get; private set; }

    /// <summary>Fixed when the line is created: an admission line never turns into a visa line.</summary>
    public ChecklistStage Stage { get; private set; }

    public DestinationCountry? Country { get; private set; }
    public ApplicantLevel? Level { get; private set; }
    public Guid? InstitutionId { get; private set; }
    public bool IsRequired { get; private set; }
    public bool IsActive { get; private set; }

    public int Specificity => (InstitutionId is null ? 0 : 4) + (Country is null ? 0 : 2) + (Level is null ? 0 : 1);

    public bool AppliesTo(DestinationCountry country, ApplicantLevel level, Guid institutionId) =>
        IsActive && (Country is null || Country == country) && (Level is null || Level == level) && (InstitutionId is null || InstitutionId == institutionId);

    public void Update(DestinationCountry? country, ApplicantLevel? level, Guid? institutionId, bool isRequired, bool isActive)
    {
        Country = country;
        Level = level;
        InstitutionId = institutionId;
        IsRequired = isRequired;
        IsActive = isActive;
    }
}

public static class ChecklistRules
{
    /// <summary>The document types an application needs (or is told it doesn't), from the matching template lines.</summary>
    public static IReadOnlyList<(Guid DocumentTypeId, bool IsRequired)> Resolve(IEnumerable<DocumentRequirement> lines, DestinationCountry country, ApplicantLevel level, Guid institutionId) =>
        lines.Where(l => l.AppliesTo(country, level, institutionId))
            .GroupBy(l => l.DocumentTypeId)
            .Select(g => (g.Key, g.OrderByDescending(l => l.Specificity).ThenByDescending(l => l.IsRequired).First().IsRequired))
            .ToList();
}

public static class ChecklistLevels
{
    /// <summary>
    /// What a candidate is asked for from their level and (if known) country alone, before any institution is chosen.
    /// Lines tied to one institution don't apply yet; they join the checklist when an application to it is made.
    /// </summary>
    public static IReadOnlyList<(Guid DocumentTypeId, bool IsRequired)> ResolveBeforeInstitution(IEnumerable<DocumentRequirement> lines, DestinationCountry? country, ApplicantLevel level) =>
        lines.Where(l => l.IsActive && l.InstitutionId is null && (l.Country is null || l.Country == country) && (l.Level is null || l.Level == level))
            .GroupBy(l => l.DocumentTypeId)
            .Select(g => (g.Key, g.OrderByDescending(l => l.Specificity).ThenByDescending(l => l.IsRequired).First().IsRequired))
            .ToList();
}

public enum ChecklistSource { Template, Manual }

/// <summary>
/// One item on an application's checklist. Copied from the template when the application is created, so later
/// template edits never rewrite an existing application; staff can refresh it or adjust single items.
/// </summary>
public class ApplicationChecklistItem : Entity
{
    private ApplicationChecklistItem() { }

    public ApplicationChecklistItem(Guid applicationId, Guid documentTypeId, bool isRequired, ChecklistSource source, string? note, ChecklistStage stage = ChecklistStage.Admission)
    {
        Stage = stage;
        ApplicationId = applicationId;
        DocumentTypeId = documentTypeId;
        IsRequired = isRequired;
        Source = source;
        Note = note;
    }

    public Guid ApplicationId { get; private set; }
    public Guid DocumentTypeId { get; private set; }
    public ChecklistStage Stage { get; private set; }
    public bool IsRequired { get; private set; }
    public ChecklistSource Source { get; private set; }

    /// <summary>Why staff changed this item (a waiver, an extra request).</summary>
    public string? Note { get; private set; }

    public void Set(bool isRequired, ChecklistSource source, string? note)
    {
        IsRequired = isRequired;
        Source = source;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}

/// <summary>One document slot for a candidate (and an application, for application-scoped types), with all its uploaded versions.</summary>
public class CandidateDocument : Entity
{
    private readonly List<DocumentVersion> _versions = [];

    private CandidateDocument() { }

    public CandidateDocument(Guid candidateId, Guid documentTypeId, Guid? applicationId)
    {
        CandidateId = candidateId;
        DocumentTypeId = documentTypeId;
        ApplicationId = applicationId;
        Status = DocumentStatus.Uploaded;
    }

    public Guid CandidateId { get; private set; }
    public Guid DocumentTypeId { get; private set; }

    /// <summary>Set only for application-scoped types.</summary>
    public Guid? ApplicationId { get; private set; }

    public DocumentStatus Status { get; private set; }
    public int CurrentVersion { get; private set; }
    public IReadOnlyList<DocumentVersion> Versions => _versions;

    /// <summary>The file version being reviewed. Earlier versions are kept.</summary>
    public DocumentVersion NewVersion(string storageKey, string fileName, string contentType, long sizeBytes, string sha256, DateOnly? expiresOn, Guid uploadedByUserId, string? uploadedByEmail, DateTime nowUtc, bool byCandidate)
    {
        // Once staff have accepted a document the candidate can't swap it; staff still can, on their behalf.
        if (byCandidate && Status == DocumentStatus.Accepted) throw new DomainException("This document has been accepted. Ask your counsellor if it needs replacing.");
        var version = new DocumentVersion(Id, CurrentVersion + 1, storageKey, fileName, contentType, sizeBytes, sha256, expiresOn, uploadedByUserId, uploadedByEmail, nowUtc);
        CurrentVersion = version.VersionNumber;
        Status = DocumentStatus.Uploaded;
        return version;
    }

    /// <summary>Records a reviewer's decision on the current version.</summary>
    public DocumentReview Review(DocumentStatus decision, string? note, Guid reviewerUserId, string? reviewerEmail, DateTime nowUtc)
    {
        if (CurrentVersion == 0) throw new DomainException("There is no file to review.");
        if (decision == DocumentStatus.Uploaded) throw new DomainException("Choose under review, accepted, or needs resubmission.");
        if (decision == Status) throw new DomainException("The document already has that status.");
        if (Status == DocumentStatus.NeedsResubmission) throw new DomainException("Wait for the candidate to upload a new version.");
        if (decision == DocumentStatus.NeedsResubmission && string.IsNullOrWhiteSpace(note)) throw new DomainException("Tell the candidate what to fix.");
        Status = decision;
        return new DocumentReview(Id, CurrentVersion, decision, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), reviewerUserId, reviewerEmail, nowUtc);
    }
}

/// <summary>One uploaded file. Never changed or deleted: a replacement is a new version.</summary>
public class DocumentVersion : Entity
{
    private DocumentVersion() { }

    internal DocumentVersion(Guid documentId, int versionNumber, string storageKey, string fileName, string contentType, long sizeBytes, string sha256, DateOnly? expiresOn, Guid uploadedByUserId, string? uploadedByEmail, DateTime uploadedAtUtc)
    {
        DocumentId = documentId;
        VersionNumber = versionNumber;
        StorageKey = storageKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        ExpiresOn = expiresOn;
        UploadedByUserId = uploadedByUserId;
        UploadedByEmail = uploadedByEmail;
        UploadedAtUtc = uploadedAtUtc;
        CreatedAtUtc = uploadedAtUtc;
    }

    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }

    /// <summary>Where the file is in private object storage. Never shown to users; the name they see is FileName.</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = string.Empty;
    public DateOnly? ExpiresOn { get; private set; }
    public Guid UploadedByUserId { get; private set; }
    public string? UploadedByEmail { get; private set; }
    public DateTime UploadedAtUtc { get; private set; }
}

/// <summary>A reviewer's decision about one version. Never edited.</summary>
public class DocumentReview : Entity
{
    private DocumentReview() { }

    internal DocumentReview(Guid documentId, int versionNumber, DocumentStatus decision, string? note, Guid reviewerUserId, string? reviewerEmail, DateTime reviewedAtUtc)
    {
        DocumentId = documentId;
        VersionNumber = versionNumber;
        Decision = decision;
        Note = note;
        ReviewerUserId = reviewerUserId;
        ReviewerEmail = reviewerEmail;
        ReviewedAtUtc = reviewedAtUtc;
        CreatedAtUtc = reviewedAtUtc;
    }

    public Guid DocumentId { get; private set; }
    public int VersionNumber { get; private set; }
    public DocumentStatus Decision { get; private set; }
    public string? Note { get; private set; }
    public Guid ReviewerUserId { get; private set; }
    public string? ReviewerEmail { get; private set; }
    public DateTime ReviewedAtUtc { get; private set; }
}
