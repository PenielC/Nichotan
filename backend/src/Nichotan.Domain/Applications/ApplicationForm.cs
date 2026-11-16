using System.Text.Json;
using System.Text.Json.Serialization;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Applications;

public enum FormStatus
{
    Draft,
    Submitted,

    /// <summary>Staff sent a submitted form back for corrections; the candidate edits and signs again.</summary>
    Reopened,
}

public enum Gender { Male, Female }

public enum DestinationCountry { Canada, Germany }

/// <summary>The program levels on the Nichotan form.</summary>
public enum ProgramLevel
{
    /// <summary>Applying to attend high school (secondary school) abroad.</summary>
    HighSchool,
    Bachelors,
    Diploma,
    Certificate,
    Masters,
    PostGraduateDiplomaOrCertificate,
}

public enum ApplicantLevel { HighSchool, Undergraduate, Postgraduate }

public enum StartTerm { Winter, Summer, Fall }

public enum HighSchoolGrade { LessThan12, TwelveOrEquivalent }

public static class ProgramLevels
{
    /// <summary>Document requirements differ for undergraduate and postgraduate applicants (BRD 6.2).</summary>
    public static ApplicantLevel ApplicantLevelOf(ProgramLevel level) => level switch
    {
        ProgramLevel.HighSchool => ApplicantLevel.HighSchool,
        ProgramLevel.Masters or ProgramLevel.PostGraduateDiplomaOrCertificate => ApplicantLevel.Postgraduate,
        _ => ApplicantLevel.Undergraduate,
    };
}

// ─────────────────────────── The form's content ───────────────────────────
// Stored as one JSON document per saved state, so every submitted version is an exact snapshot. A draft may be
// incomplete; completeness is checked on submit.

public class PersonalDetails
{
    public string? FirstName { get; set; }
    public string? MiddleNames { get; set; }
    public string? LastName { get; set; }
    public Gender? Gender { get; set; }
    public DateOnly? BirthDate { get; set; }
    public string? CitizenshipCountry { get; set; }
}

public class ContactDetails
{
    public string? HomeAddress { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? AlternativePhone { get; set; }
    public string? NextOfKinName { get; set; }
    public string? NextOfKinEmail { get; set; }
    public string? NextOfKinPhone { get; set; }
}

public class ProgramSelection
{
    public DestinationCountry? DestinationCountry { get; set; }
    public ProgramLevel? ProgramLevel { get; set; }
    public int? StartYear { get; set; }
    public StartTerm? StartTerm { get; set; }

    /// <summary>Up to 5 institutions, in order of preference.</summary>
    public List<string> Institutions { get; set; } = [];

    /// <summary>Up to 2 programme choices.</summary>
    public List<string> ProgramChoices { get; set; } = [];
}

public class HighSchoolEntry
{
    public string? Name { get; set; }
    public string? ProvinceCountry { get; set; }

    /// <summary>"yyyy-MM".</summary>
    public string? Started { get; set; }

    /// <summary>"yyyy-MM"; empty while still attending.</summary>
    public string? Completed { get; set; }

    public HighSchoolGrade? Grade { get; set; }
}

public class PostSecondaryEntry
{
    public string? Institution { get; set; }
    public string? ProvinceCountry { get; set; }
    public string? Started { get; set; }
    public string? Completed { get; set; }
    public string? CredentialAwarded { get; set; }
    public string? CredentialAwardedOn { get; set; }
}

public class AcademicHistory
{
    /// <summary>Most recent first, up to 2.</summary>
    public List<HighSchoolEntry> HighSchools { get; set; } = [];

    /// <summary>Most recent first, up to 3.</summary>
    public List<PostSecondaryEntry> PostSecondary { get; set; } = [];
}

public class ApplicationFormData
{
    public PersonalDetails Personal { get; set; } = new();
    public ContactDetails Contact { get; set; } = new();
    public ProgramSelection Program { get; set; } = new();
    public AcademicHistory Academic { get; set; } = new();
}

public static class FormJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serialize(ApplicationFormData data) => JsonSerializer.Serialize(data, Options);

    public static ApplicationFormData Deserialize(string json) => JsonSerializer.Deserialize<ApplicationFormData>(json, Options) ?? new ApplicationFormData();
}

/// <summary>The declaration candidates sign. Changing the wording means a new version number, so old signatures keep the text they agreed to.</summary>
public static class Declaration
{
    public const string CurrentVersion = "1";

    public const string Text = "By signing this Application, I understand and I certify that all statements on this application are true and complete.";
}

// ─────────────────────────── The form and its history ───────────────────────────

/// <summary>A candidate's Nichotan application form: one per candidate, editable while Draft or Reopened, locked once Submitted.</summary>
public class ApplicationForm : Entity
{
    private readonly List<ApplicationFormVersion> _versions = [];

    private ApplicationForm() { }

    public ApplicationForm(Guid candidateId, ApplicationFormData initial)
    {
        CandidateId = candidateId;
        Status = FormStatus.Draft;
        DataJson = FormJson.Serialize(initial);
    }

    public Guid CandidateId { get; private set; }
    public FormStatus Status { get; private set; }

    /// <summary>The working copy. Once submitted it equals the latest version.</summary>
    public string DataJson { get; private set; } = "{}";

    /// <summary>The number of the latest submitted version (0 before the first submission).</summary>
    public int CurrentVersion { get; private set; }

    public DateTime? SubmittedAtUtc { get; private set; }
    public string? ReopenReason { get; private set; }
    public DateTime? ReopenedAtUtc { get; private set; }
    public Guid? ReopenedByUserId { get; private set; }

    public IReadOnlyList<ApplicationFormVersion> Versions => _versions;

    public bool IsEditable => Status is FormStatus.Draft or FormStatus.Reopened;

    public void SaveDraft(ApplicationFormData data)
    {
        if (!IsEditable) throw new DomainException("This form has been submitted and can't be changed. Ask your counsellor if something needs correcting.");
        DataJson = FormJson.Serialize(data);
    }

    /// <summary>
    /// Locks the form and returns an immutable, signed snapshot of it. The caller adds the version to the
    /// database set itself (adding it only through this collection would make EF treat its preset id as an existing row).
    /// </summary>
    public ApplicationFormVersion Submit(Guid submittedByUserId, DateTime nowUtc, string contentHash, SignatureEvidence signature)
    {
        if (!IsEditable) throw new DomainException("This form has already been submitted.");
        var version = new ApplicationFormVersion(Id, CurrentVersion + 1, DataJson, contentHash, nowUtc, submittedByUserId, signature);
        CurrentVersion = version.VersionNumber;
        Status = FormStatus.Submitted;
        SubmittedAtUtc = nowUtc;
        ReopenReason = null;
        ReopenedAtUtc = null;
        ReopenedByUserId = null;
        return version;
    }

    /// <summary>Unlocks a submitted form for corrections. Earlier versions are kept untouched.</summary>
    public void Reopen(Guid staffUserId, string reason, DateTime nowUtc)
    {
        if (Status != FormStatus.Submitted) throw new DomainException("Only a submitted form can be reopened.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Give a reason, so the candidate knows what to correct.");
        Status = FormStatus.Reopened;
        ReopenReason = reason.Trim();
        ReopenedAtUtc = nowUtc;
        ReopenedByUserId = staffUserId;
    }
}

/// <summary>Who signed, how, and when. The provider name says which signing method produced it, so another one can be added later.</summary>
public class SignatureEvidence
{
    private SignatureEvidence() { }

    public SignatureEvidence(string provider, string signerName, DateTime signedAtUtc, string? ipAddress, byte[]? image, string? externalReference, string declarationVersion, string declarationText)
    {
        Provider = provider;
        SignerName = signerName;
        SignedAtUtc = signedAtUtc;
        IpAddress = ipAddress;
        Image = image;
        ExternalReference = externalReference;
        DeclarationVersion = declarationVersion;
        DeclarationText = declarationText;
    }

    /// <summary>"Native" for the built-in drawn signature; a provider's name for a third-party one.</summary>
    public string Provider { get; private set; } = string.Empty;

    public string SignerName { get; private set; } = string.Empty;
    public DateTime SignedAtUtc { get; private set; }
    public string? IpAddress { get; private set; }

    /// <summary>The drawn signature (PNG).</summary>
    public byte[]? Image { get; private set; }

    /// <summary>A third-party provider's envelope or certificate reference.</summary>
    public string? ExternalReference { get; private set; }

    public string DeclarationVersion { get; private set; } = string.Empty;
    public string DeclarationText { get; private set; } = string.Empty;
}

/// <summary>One submission. Never edited or deleted.</summary>
public class ApplicationFormVersion : Entity
{
    private ApplicationFormVersion() { }

    internal ApplicationFormVersion(Guid formId, int versionNumber, string dataJson, string contentHash, DateTime submittedAtUtc, Guid submittedByUserId, SignatureEvidence signature)
    {
        FormId = formId;
        VersionNumber = versionNumber;
        DataJson = dataJson;
        ContentHash = contentHash;
        SubmittedAtUtc = submittedAtUtc;
        SubmittedByUserId = submittedByUserId;
        Signature = signature;
    }

    public Guid FormId { get; private set; }
    public int VersionNumber { get; private set; }
    public string DataJson { get; private set; } = "{}";

    /// <summary>SHA-256 over the data and declaration, tying the signature to exactly this content.</summary>
    public string ContentHash { get; private set; } = string.Empty;

    public DateTime SubmittedAtUtc { get; private set; }
    public Guid SubmittedByUserId { get; private set; }
    public SignatureEvidence Signature { get; private set; } = null!;
}
