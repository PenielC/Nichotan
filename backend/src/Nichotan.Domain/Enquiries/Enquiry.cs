using Nichotan.Domain.Common;

namespace Nichotan.Domain.Enquiries;

/// <summary>The highest qualification a person has finished.</summary>
public enum Qualification
{
    SecondarySchool,
    AdvancedLevel,
    Certificate,
    Diploma,
    Bachelors,
    Masters,
    Doctorate,
    Other,
}

/// <summary>How open the person is to Nichotan's recommended destinations, Canada and Germany.</summary>
public enum DestinationInterest
{
    Both,
    CanadaOnly,
    GermanyOnly,
    Other,
}

public enum EnquiryStatus { New, Contacted, Closed }

/// <summary>
/// Someone who asked about studying abroad through the public landing page. They have no account, so this holds
/// only what they chose to tell us, and is kept apart from candidates until a counsellor follows it up.
/// </summary>
public class Enquiry : Entity
{
    private Enquiry() { }

    public Enquiry(
        string name,
        string? email,
        string? phone,
        Qualification highestQualification,
        int graduationYear,
        string? preferredProgramme,
        bool openToGuidance,
        DestinationInterest destinationInterest,
        string? message)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Tell us your name.");
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)) throw new DomainException("Give an email address or a phone number so we can reach you.");
        if (string.IsNullOrWhiteSpace(preferredProgramme) && !openToGuidance) throw new DomainException("Name a programme, or say you'd like guidance.");

        Name = name.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        HighestQualification = highestQualification;
        GraduationYear = graduationYear;
        PreferredProgramme = string.IsNullOrWhiteSpace(preferredProgramme) ? null : preferredProgramme.Trim();
        OpenToGuidance = openToGuidance;
        DestinationInterest = destinationInterest;
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        Status = EnquiryStatus.New;
    }

    public string Name { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public Qualification HighestQualification { get; private set; }

    /// <summary>The year they graduated, or expect to.</summary>
    public int GraduationYear { get; private set; }

    public string? PreferredProgramme { get; private set; }

    /// <summary>They'd like help choosing a programme.</summary>
    public bool OpenToGuidance { get; private set; }

    public DestinationInterest DestinationInterest { get; private set; }
    public string? Message { get; private set; }
    public EnquiryStatus Status { get; private set; }

    /// <summary>Staff's own note on the follow-up.</summary>
    public string? StaffNote { get; private set; }

    public DateTime? StatusChangedAtUtc { get; private set; }
    public string? HandledByEmail { get; private set; }

    /// <summary>When an administrator or counsellor first opened it. Empty until then.</summary>
    public DateTime? FirstViewedAtUtc { get; private set; }

    public string? FirstViewedByEmail { get; private set; }

    /// <summary>Records the first time it is opened for review. Later views change nothing. Returns whether this was the first.</summary>
    public bool MarkViewed(string? staffEmail, DateTime nowUtc)
    {
        if (FirstViewedAtUtc is not null) return false;
        FirstViewedAtUtc = nowUtc;
        FirstViewedByEmail = staffEmail;
        return true;
    }

    public void Update(EnquiryStatus status, string? staffNote, string? staffEmail, DateTime nowUtc)
    {
        Status = status;
        StaffNote = string.IsNullOrWhiteSpace(staffNote) ? null : staffNote.Trim();
        StatusChangedAtUtc = nowUtc;
        HandledByEmail = staffEmail;
    }
}
