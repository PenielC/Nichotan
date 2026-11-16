using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Admissions;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;
using Nichotan.Domain.Visas;

namespace Nichotan.Application.Visas;

// The pre-departure list. Staff activate it for an approved visa case; the student confirms each item, staff verify it.
// The wording of each country's list is a template an administrator keeps current.

public record PreDepartureItemResult(
    Guid Id,
    string Title,
    string? Detail,
    PreDepartureImportance Importance,
    PreDepartureSource Source,
    bool IsActive,
    string? Note,
    DateTime? ConfirmedAtUtc,
    DateTime? VerifiedAtUtc,
    string? VerifiedBy,
    DateTime? CompletedAtUtc);

public record PreDepartureResult(
    Guid VisaCaseId,
    DestinationCountry Country,
    string InstitutionName,
    bool Activated,
    bool CanActivate,
    DateOnly? PlannedDepartureOn,
    string? Caveat,
    bool ReadyToTravel,
    IReadOnlyList<PreDepartureItemResult> Items);

public record PreDepartureTemplateItemResult(Guid Id, DestinationCountry Country, string Title, string? Detail, PreDepartureImportance Importance, int SortOrder, bool IsActive);

public record PreDepartureCaveatResult(DestinationCountry Country, string Text);

public record PreDepartureTemplateResult(IReadOnlyList<PreDepartureTemplateItemResult> Items, IReadOnlyList<PreDepartureCaveatResult> Caveats);

internal static class PreDepartureMapping
{
    public static async Task<PreDepartureResult> BuildAsync(IAppDbContext db, Guid visaCaseId, bool forStudent, CancellationToken cancellationToken)
    {
        var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visaCaseId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        var row = await VisaMapping.Rows(db, db.VisaCases.AsNoTracking().Where(v => v.Id == visaCaseId)).FirstAsync(cancellationToken);
        var list = await db.PreDepartureLists.AsNoTracking().FirstOrDefaultAsync(l => l.VisaCaseId == visaCaseId, cancellationToken);
        var caveat = await db.PreDepartureCaveats.AsNoTracking().Where(c => c.Country == row.Country).Select(c => c.Text).FirstOrDefaultAsync(cancellationToken);
        var items = list is null ? [] : await db.PreDepartureItems.AsNoTracking().Where(i => i.ListId == list.Id).OrderBy(i => i.SortOrder).ThenBy(i => i.CreatedAtUtc).ToListAsync(cancellationToken);
        // Students don't see items staff switched off.
        var shown = forStudent ? items.Where(i => i.IsActive).ToList() : items;
        var ready = list is not null && items.Where(i => i.IsActive && i.Importance == PreDepartureImportance.Required).All(i => i.CompletedAtUtc is not null) && items.Any(i => i.IsActive);
        return new PreDepartureResult(
            visa.Id, row.Country, row.InstitutionName, list is not null, list is null && visa.Status == VisaStatus.Approved, list?.PlannedDepartureOn, caveat, ready,
            shown.Select(i => new PreDepartureItemResult(i.Id, i.Title, i.Detail, i.Importance, i.Source, i.IsActive, i.Note, i.ConfirmedAtUtc, i.VerifiedAtUtc, forStudent ? null : i.VerifiedByEmail, i.CompletedAtUtc)).ToList());
    }

    /// <summary>Loads an item and the visa case it belongs to, checking the case is the caller's own when a student is asking.</summary>
    public static async Task<(PreDepartureItem Item, PreDepartureList List, VisaCase Visa)> LoadItemAsync(IAppDbContext db, Guid itemId, Guid? visaCaseId, Guid? onlyCandidateId, CancellationToken cancellationToken)
    {
        var item = await db.PreDepartureItems.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken) ?? throw new NotFoundException("Item not found.");
        var list = await db.PreDepartureLists.AsNoTracking().FirstAsync(l => l.Id == item.ListId, cancellationToken);
        var visa = await db.VisaCases.AsNoTracking().FirstAsync(v => v.Id == list.VisaCaseId, cancellationToken);
        if ((visaCaseId is { } id && visa.Id != id) || (onlyCandidateId is { } candidate && visa.CandidateId != candidate)) throw new NotFoundException("Item not found.");
        return (item, list, visa);
    }
}

public record GetPreDepartureQuery(Guid VisaCaseId) : IQuery<PreDepartureResult>;

public class GetPreDepartureHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetPreDepartureQuery, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(GetPreDepartureQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await PreDepartureMapping.BuildAsync(db, query.VisaCaseId, forStudent: false, cancellationToken);
    }
}

/// <summary>Copies the country's template onto an approved visa case, and tells the student their list is ready.</summary>
public record ActivatePreDepartureCommand(Guid VisaCaseId) : ICommand<PreDepartureResult>;

public class ActivatePreDepartureHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock, IEmailSender email, IAppUrls urls, ILogger<ActivatePreDepartureHandler> logger)
    : ICommandHandler<ActivatePreDepartureCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(ActivatePreDepartureCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.Id == command.VisaCaseId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        if (visa.Status != VisaStatus.Approved) throw new DomainException("The pre-departure list opens once the visa has been approved.");
        if (await db.PreDepartureLists.AnyAsync(l => l.VisaCaseId == visa.Id, cancellationToken)) throw new DomainException("This list is already active.");
        var country = await db.AdmissionApplications.AsNoTracking().Where(a => a.Id == visa.ApplicationId).Select(a => a.Country).FirstAsync(cancellationToken);
        var template = await db.PreDepartureTemplate.AsNoTracking().Where(t => t.Country == country && t.IsActive).OrderBy(t => t.SortOrder).ToListAsync(cancellationToken);
        if (template.Count == 0) throw new DomainException($"There is no pre-departure list for {country} yet. An administrator adds one under Checklists.");

        var list = new PreDepartureList(visa.Id, AdmissionMapping.Today(clock), current.Email);
        db.PreDepartureLists.Add(list);
        foreach (var t in template) db.PreDepartureItems.Add(new PreDepartureItem(list.Id, t.Title, t.Detail, t.Importance, PreDepartureSource.Template, t.SortOrder));
        audit.Add(AuditAction.PreDepartureActivated, entityType: nameof(VisaCase), entityId: visa.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);

        var profile = await db.CandidateProfiles.AsNoTracking().FirstAsync(p => p.Id == visa.CandidateId, cancellationToken);
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.PreDepartureReady(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/applications"), logger, cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, visa.Id, forStudent: false, cancellationToken);
    }
}

public record AddPreDepartureItemCommand(Guid VisaCaseId, string Title, string? Detail, PreDepartureImportance Importance) : ICommand<PreDepartureResult>;

public class AddPreDepartureItemValidator : AbstractValidator<AddPreDepartureItemCommand>
{
    public AddPreDepartureItemValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Detail).MaximumLength(500);
    }
}

public class AddPreDepartureItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<AddPreDepartureItemCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(AddPreDepartureItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var list = await db.PreDepartureLists.AsNoTracking().FirstOrDefaultAsync(l => l.VisaCaseId == command.VisaCaseId, cancellationToken) ?? throw new DomainException("Activate the list first.");
        var next = (await db.PreDepartureItems.AsNoTracking().Where(i => i.ListId == list.Id).Select(i => (int?)i.SortOrder).MaxAsync(cancellationToken) ?? 0) + 10;
        db.PreDepartureItems.Add(new PreDepartureItem(list.Id, command.Title, command.Detail, command.Importance, PreDepartureSource.Manual, next));
        audit.Add(AuditAction.PreDepartureItemChanged, entityType: nameof(VisaCase), entityId: command.VisaCaseId.ToString(), details: new { change = "added" });
        await db.SaveChangesAsync(cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, command.VisaCaseId, forStudent: false, cancellationToken);
    }
}

/// <summary>Staff edit one item: a note, how important it is, or switch it off for this student.</summary>
public record UpdatePreDepartureItemCommand(Guid VisaCaseId, Guid ItemId, string? Note, PreDepartureImportance Importance, bool IsActive) : ICommand<PreDepartureResult>;

public class UpdatePreDepartureItemValidator : AbstractValidator<UpdatePreDepartureItemCommand>
{
    public UpdatePreDepartureItemValidator() => RuleFor(x => x.Note).MaximumLength(500);
}

public class UpdatePreDepartureItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<UpdatePreDepartureItemCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(UpdatePreDepartureItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var (item, _, _) = await PreDepartureMapping.LoadItemAsync(db, command.ItemId, command.VisaCaseId, null, cancellationToken);
        item.Update(command.Note, command.Importance, command.IsActive);
        audit.Add(AuditAction.PreDepartureItemChanged, entityType: nameof(VisaCase), entityId: command.VisaCaseId.ToString(), details: new { change = command.IsActive ? "updated" : "switched off", item = item.Title });
        await db.SaveChangesAsync(cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, command.VisaCaseId, forStudent: false, cancellationToken);
    }
}

/// <summary>Staff check an item (the student has it, or they've seen it) or undo that.</summary>
public record VerifyPreDepartureItemCommand(Guid VisaCaseId, Guid ItemId, bool Verified) : ICommand<PreDepartureResult>;

public class VerifyPreDepartureItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<VerifyPreDepartureItemCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(VerifyPreDepartureItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var (item, _, _) = await PreDepartureMapping.LoadItemAsync(db, command.ItemId, command.VisaCaseId, null, cancellationToken);
        item.SetVerified(command.Verified, current.Email, clock.UtcNow);
        audit.Add(AuditAction.PreDepartureItemChanged, entityType: nameof(VisaCase), entityId: command.VisaCaseId.ToString(), details: new { change = command.Verified ? "verified" : "unverified", item = item.Title });
        await db.SaveChangesAsync(cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, command.VisaCaseId, forStudent: false, cancellationToken);
    }
}

/// <summary>The planned departure date. The student or staff can set it.</summary>
public record SetDepartureDateCommand(Guid VisaCaseId, DateOnly? PlannedOn) : ICommand<PreDepartureResult>;

public class SetDepartureDateHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<SetDepartureDateCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(SetDepartureDateCommand command, CancellationToken cancellationToken)
    {
        var byStudent = current.IsInRole(Roles.Candidate);
        if (byStudent) current.RequireAny(Roles.Candidate);
        else current.RequireAny(Guard.VisaStaff);
        var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.Id == command.VisaCaseId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        if (byStudent && visa.CandidateId != await StudentApplications.ProfileIdAsync(db, current, cancellationToken)) throw new NotFoundException("Visa case not found.");
        var list = await db.PreDepartureLists.FirstOrDefaultAsync(l => l.VisaCaseId == visa.Id, cancellationToken) ?? throw new DomainException("The pre-departure list isn't active yet.");
        list.SetDeparture(command.PlannedOn, AdmissionMapping.Today(clock));
        audit.Add(AuditAction.PreDepartureItemChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { change = "departure date" });
        await db.SaveChangesAsync(cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, visa.Id, forStudent: byStudent, cancellationToken);
    }
}

// ─────────────────────────── The student's own lists ───────────────────────────

public record GetMyPreDepartureQuery : IQuery<IReadOnlyList<PreDepartureResult>>;

public class GetMyPreDepartureHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyPreDepartureQuery, IReadOnlyList<PreDepartureResult>>
{
    public async ValueTask<IReadOnlyList<PreDepartureResult>> Handle(GetMyPreDepartureQuery query, CancellationToken cancellationToken)
    {
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var visaIds = await db.VisaCases.AsNoTracking().Where(v => v.CandidateId == candidateId && db.PreDepartureLists.Any(l => l.VisaCaseId == v.Id)).Select(v => v.Id).ToListAsync(cancellationToken);
        var results = new List<PreDepartureResult>();
        foreach (var id in visaIds) results.Add(await PreDepartureMapping.BuildAsync(db, id, forStudent: true, cancellationToken));
        return results;
    }
}

/// <summary>The student says they have this item (or takes that back, until staff have checked it).</summary>
public record ConfirmMyPreDepartureItemCommand(Guid ItemId, bool Confirmed) : ICommand<PreDepartureResult>;

public class ConfirmMyPreDepartureItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<ConfirmMyPreDepartureItemCommand, PreDepartureResult>
{
    public async ValueTask<PreDepartureResult> Handle(ConfirmMyPreDepartureItemCommand command, CancellationToken cancellationToken)
    {
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var (item, _, visa) = await PreDepartureMapping.LoadItemAsync(db, command.ItemId, null, candidateId, cancellationToken);
        item.SetConfirmed(command.Confirmed, clock.UtcNow);
        audit.Add(AuditAction.PreDepartureItemChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { change = command.Confirmed ? "confirmed" : "unconfirmed", by = "student" });
        await db.SaveChangesAsync(cancellationToken);
        return await PreDepartureMapping.BuildAsync(db, visa.Id, forStudent: true, cancellationToken);
    }
}

// ─────────────────────────── The country templates (administrators) ───────────────────────────

public record GetPreDepartureTemplateQuery : IQuery<PreDepartureTemplateResult>;

public class GetPreDepartureTemplateHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetPreDepartureTemplateQuery, PreDepartureTemplateResult>
{
    public async ValueTask<PreDepartureTemplateResult> Handle(GetPreDepartureTemplateQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var items = await db.PreDepartureTemplate.AsNoTracking().OrderBy(t => t.Country).ThenBy(t => t.SortOrder).ToListAsync(cancellationToken);
        var caveats = await db.PreDepartureCaveats.AsNoTracking().OrderBy(c => c.Country).ToListAsync(cancellationToken);
        return new PreDepartureTemplateResult(
            items.Select(t => new PreDepartureTemplateItemResult(t.Id, t.Country, t.Title, t.Detail, t.Importance, t.SortOrder, t.IsActive)).ToList(),
            caveats.Select(c => new PreDepartureCaveatResult(c.Country, c.Text)).ToList());
    }
}

public record SavePreDepartureTemplateItemCommand(Guid? Id, DestinationCountry Country, string Title, string? Detail, PreDepartureImportance Importance, int SortOrder, bool IsActive) : ICommand<PreDepartureTemplateItemResult>;

public class SavePreDepartureTemplateItemValidator : AbstractValidator<SavePreDepartureTemplateItemCommand>
{
    public SavePreDepartureTemplateItemValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Detail).MaximumLength(500);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }
}

public class SavePreDepartureTemplateItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SavePreDepartureTemplateItemCommand, PreDepartureTemplateItemResult>
{
    public async ValueTask<PreDepartureTemplateItemResult> Handle(SavePreDepartureTemplateItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        PreDepartureTemplateItem item;
        if (command.Id is { } id)
        {
            item = await db.PreDepartureTemplate.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Item not found.");
            if (item.Country != command.Country) throw new DomainException("An item can't move to another country's list. Add a new one instead.");
            item.Update(command.Title, command.Detail, command.Importance, command.SortOrder, command.IsActive);
        }
        else
        {
            item = new PreDepartureTemplateItem(command.Country, command.Title, command.Detail, command.Importance, command.SortOrder);
            if (!command.IsActive) item.Update(command.Title, command.Detail, command.Importance, command.SortOrder, false);
            db.PreDepartureTemplate.Add(item);
        }

        // Lists already activated keep the items they were given.
        audit.Add(AuditAction.PreDepartureTemplateChanged, entityType: nameof(PreDepartureTemplateItem), entityId: item.Id.ToString(), details: new { change = command.Id is null ? "created" : "updated" });
        await db.SaveChangesAsync(cancellationToken);
        return new PreDepartureTemplateItemResult(item.Id, item.Country, item.Title, item.Detail, item.Importance, item.SortOrder, item.IsActive);
    }
}

public record SavePreDepartureCaveatCommand(DestinationCountry Country, string Text) : ICommand<PreDepartureCaveatResult>;

public class SavePreDepartureCaveatValidator : AbstractValidator<SavePreDepartureCaveatCommand>
{
    public SavePreDepartureCaveatValidator() => RuleFor(x => x.Text).NotEmpty().MaximumLength(1500);
}

public class SavePreDepartureCaveatHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SavePreDepartureCaveatCommand, PreDepartureCaveatResult>
{
    public async ValueTask<PreDepartureCaveatResult> Handle(SavePreDepartureCaveatCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var caveat = await db.PreDepartureCaveats.FirstOrDefaultAsync(c => c.Country == command.Country, cancellationToken);
        if (caveat is null)
        {
            caveat = new PreDepartureCaveat(command.Country, command.Text);
            db.PreDepartureCaveats.Add(caveat);
        }
        else
        {
            caveat.Update(command.Text);
        }

        audit.Add(AuditAction.PreDepartureTemplateChanged, entityType: nameof(PreDepartureCaveat), entityId: caveat.Id.ToString(), details: new { change = "wording" });
        await db.SaveChangesAsync(cancellationToken);
        return new PreDepartureCaveatResult(caveat.Country, caveat.Text);
    }
}
