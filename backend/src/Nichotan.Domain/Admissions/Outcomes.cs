using Nichotan.Domain.Applications;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Admissions;

// An offer and a formal acceptance are separate things that happen at different times, so each has its own record.
// Neither is inferred from the other: an offer doesn't mean the school has sent its formal acceptance letter. Staff enter
// both from what the school told them. Any deposit is between the student and the school and isn't tracked here.

/// <summary>The school's offer. When the offer arrived is on the application's history; this holds its details. The letter is a stored document.</summary>
public class AdmissionOffer : Entity
{
    private AdmissionOffer() { }

    public AdmissionOffer(Guid applicationId, string? conditions, DateOnly? respondByOn, string? recordedByEmail)
    {
        ApplicationId = applicationId;
        RecordedByEmail = recordedByEmail;
        Update(conditions, respondByOn);
    }

    public Guid ApplicationId { get; private set; }

    /// <summary>What the school asks of the student before the offer is final, in the school's words.</summary>
    public string? Conditions { get; private set; }

    /// <summary>The date the school wants an answer by, if it says.</summary>
    public DateOnly? RespondByOn { get; private set; }

    public string? RecordedByEmail { get; private set; }

    public void Update(string? conditions, DateOnly? respondByOn)
    {
        Conditions = string.IsNullOrWhiteSpace(conditions) ? null : conditions.Trim();
        RespondByOn = respondByOn;
    }
}

public static class PalRules
{
    /// <summary>
    /// A Provincial Attestation Letter number goes with a Canadian acceptance letter for an undergraduate. It is optional:
    /// not every acceptance has one, and it doesn't apply to Germany or to other levels.
    /// </summary>
    public static bool Applies(DestinationCountry country, ApplicantLevel level) => country == DestinationCountry.Canada && level == ApplicantLevel.Undergraduate;

    /// <summary>The PAL should follow the acceptance letter; after this many days with none, staff are told it is overdue.</summary>
    public const int FollowUpAfterDays = 3;
}

/// <summary>The institution's formal acceptance letter has been received. Separate from the offer and the deposit.</summary>
public class AdmissionAcceptance : Entity
{
    private AdmissionAcceptance() { }

    public AdmissionAcceptance(Guid applicationId, DateOnly receivedOn, string? palNumber, string? note, string? recordedByEmail)
    {
        ApplicationId = applicationId;
        RecordedByEmail = recordedByEmail;
        Update(receivedOn, palNumber, note);
    }

    public Guid ApplicationId { get; private set; }
    public DateOnly ReceivedOn { get; private set; }

    /// <summary>The Provincial Attestation Letter number, where one applies and was given.</summary>
    public string? PalNumber { get; private set; }

    public string? Note { get; private set; }
    public string? RecordedByEmail { get; private set; }

    public void Update(DateOnly receivedOn, string? palNumber, string? note)
    {
        ReceivedOn = receivedOn;
        PalNumber = string.IsNullOrWhiteSpace(palNumber) ? null : palNumber.Trim().ToUpperInvariant();
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
