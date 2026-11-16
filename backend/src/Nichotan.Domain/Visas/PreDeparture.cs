using Nichotan.Domain.Applications;
using Nichotan.Domain.Common;

namespace Nichotan.Domain.Visas;

// The pre-departure ("To Carry") list a student works through after their visa is approved. Each country has its own template
// (never assume Canada's list applies to Germany); activating it for one visa case copies the template onto that case.

/// <summary>Required items are what the student really needs to travel; recommended ones are advice (warm clothing is advice, not a requirement).</summary>
public enum PreDepartureImportance { Required, Recommended }

public enum PreDepartureSource { Template, Manual }

/// <summary>One line of a country's template. Edits apply to lists activated afterwards, never to lists already activated.</summary>
public class PreDepartureTemplateItem : Entity
{
    private PreDepartureTemplateItem() { }

    public PreDepartureTemplateItem(DestinationCountry country, string title, string? detail, PreDepartureImportance importance, int sortOrder)
    {
        Country = country;
        SortOrder = sortOrder;
        IsActive = true;
        Update(title, detail, importance, sortOrder, true);
    }

    public DestinationCountry Country { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public PreDepartureImportance Importance { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, string? detail, PreDepartureImportance importance, int sortOrder, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("An item needs a title.");
        Title = title.Trim();
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        Importance = importance;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>The wording shown with a country's list. Nichotan approves it and keeps it current; it isn't legal advice.</summary>
public class PreDepartureCaveat : Entity
{
    private PreDepartureCaveat() { }

    public PreDepartureCaveat(DestinationCountry country, string text)
    {
        Country = country;
        Update(text);
    }

    public DestinationCountry Country { get; private set; }
    public string Text { get; private set; } = string.Empty;

    public void Update(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new DomainException("Write the wording to show with the list.");
        Text = text.Trim();
    }
}

/// <summary>A visa case's pre-departure list. One per case, activated by staff once the visa is approved.</summary>
public class PreDepartureList : Entity
{
    private PreDepartureList() { }

    public PreDepartureList(Guid visaCaseId, DateOnly activatedOn, string? activatedByEmail)
    {
        VisaCaseId = visaCaseId;
        ActivatedOn = activatedOn;
        ActivatedByEmail = activatedByEmail;
    }

    public Guid VisaCaseId { get; private set; }
    public DateOnly ActivatedOn { get; private set; }
    public string? ActivatedByEmail { get; private set; }

    /// <summary>When the student plans to leave, if they've said.</summary>
    public DateOnly? PlannedDepartureOn { get; private set; }

    public void SetDeparture(DateOnly? plannedOn, DateOnly today)
    {
        if (plannedOn is { } date && date < today) throw new DomainException("The departure date can't be in the past.");
        PlannedDepartureOn = plannedOn;
    }
}

/// <summary>
/// One item on a student's list. The student confirms they have it; staff verify it. An item is complete once staff have
/// verified it. Items staff don't need are switched off, not deleted.
/// </summary>
public class PreDepartureItem : Entity
{
    private PreDepartureItem() { }

    public PreDepartureItem(Guid listId, string title, string? detail, PreDepartureImportance importance, PreDepartureSource source, int sortOrder)
    {
        ListId = listId;
        Source = source;
        SortOrder = sortOrder;
        IsActive = true;
        Title = string.IsNullOrWhiteSpace(title) ? throw new DomainException("An item needs a title.") : title.Trim();
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        Importance = importance;
    }

    public Guid ListId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public PreDepartureImportance Importance { get; private set; }
    public PreDepartureSource Source { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    /// <summary>A staff note on this item, shown to the student too.</summary>
    public string? Note { get; private set; }

    public DateTime? ConfirmedAtUtc { get; private set; }
    public DateTime? VerifiedAtUtc { get; private set; }
    public string? VerifiedByEmail { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public void SetConfirmed(bool confirmed, DateTime nowUtc)
    {
        if (!IsActive) throw new DomainException("This item isn't needed.");
        if (!confirmed && VerifiedAtUtc is not null) throw new DomainException("Staff have already checked this one. Ask your counsellor if it needs undoing.");
        ConfirmedAtUtc = confirmed ? ConfirmedAtUtc ?? nowUtc : null;
    }

    public void SetVerified(bool verified, string? staffEmail, DateTime nowUtc)
    {
        if (!IsActive) throw new DomainException("This item isn't needed.");
        VerifiedAtUtc = verified ? VerifiedAtUtc ?? nowUtc : null;
        VerifiedByEmail = verified ? VerifiedByEmail ?? staffEmail : null;
        CompletedAtUtc = verified ? CompletedAtUtc ?? nowUtc : null;
    }

    public void Update(string? note, PreDepartureImportance importance, bool isActive)
    {
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Importance = importance;
        IsActive = isActive;
    }
}
