using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Documents;

namespace Nichotan.Application.Admissions;

// The school's offer and the formal acceptance letter (with the PAL number where one applies). Each is its own
// record. Files (the letters and the proof of payment) are stored through the documents module and linked here by purpose.

public record DocRef(Guid DocumentId, string FileName, DateTime UploadedAtUtc);

public record OfferResult(string? Conditions, DateOnly? RespondByOn, DateTime RecordedAtUtc, string? RecordedBy);

public record AcceptanceResult(DateOnly ReceivedOn, string? PalNumber, string? Note, DateTime RecordedAtUtc, string? RecordedBy);

public record OutcomeResult(
    Guid ApplicationId,
    AdmissionStatus Status,
    bool PalApplies,
    OfferResult? Offer,
    AcceptanceResult? Acceptance,
    DocRef? OfferLetter,
    DocRef? AcceptanceLetter,
    DocRef? PalLetter);

internal static class Outcomes
{
    public static async Task<Dictionary<(Guid ApplicationId, DocumentPurpose Purpose), DocRef>> DocsAsync(IAppDbContext db, IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken)
    {
        var types = await db.DocumentTypes.AsNoTracking().Where(t => t.Purpose != DocumentPurpose.None).ToDictionaryAsync(t => t.Id, t => t.Purpose, cancellationToken);
        var docs = await db.CandidateDocuments.AsNoTracking().Where(d => d.ApplicationId != null && applicationIds.Contains(d.ApplicationId.Value) && types.Keys.Contains(d.DocumentTypeId)).ToListAsync(cancellationToken);
        var ids = docs.Select(d => d.Id).ToList();
        var versions = await db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync(cancellationToken);
        var result = new Dictionary<(Guid, DocumentPurpose), DocRef>();
        foreach (var d in docs)
        {
            var v = versions.FirstOrDefault(x => x.DocumentId == d.Id && x.VersionNumber == d.CurrentVersion);
            if (v is not null) result[(d.ApplicationId!.Value, types[d.DocumentTypeId])] = new DocRef(d.Id, v.FileName, v.UploadedAtUtc);
        }

        return result;
    }

    public static async Task<OutcomeResult> ForAsync(IAppDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var a = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == applicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        var offer = await db.AdmissionOffers.AsNoTracking().FirstOrDefaultAsync(o => o.ApplicationId == applicationId, cancellationToken);
        var acceptance = await db.AdmissionAcceptances.AsNoTracking().FirstOrDefaultAsync(x => x.ApplicationId == applicationId, cancellationToken);
        var docs = await DocsAsync(db, [applicationId], cancellationToken);
        DocRef? Doc(DocumentPurpose p) => docs.GetValueOrDefault((applicationId, p));
        return new OutcomeResult(
            applicationId, a.Status, PalRules.Applies(a.Country, a.Level),
            offer is null ? null : new OfferResult(offer.Conditions, offer.RespondByOn, offer.CreatedAtUtc, offer.RecordedByEmail),
            acceptance is null ? null : new AcceptanceResult(acceptance.ReceivedOn, acceptance.PalNumber, acceptance.Note, acceptance.CreatedAtUtc, acceptance.RecordedByEmail),
            Doc(DocumentPurpose.OfferLetter), Doc(DocumentPurpose.AcceptanceLetter), Doc(DocumentPurpose.PalLetter));
    }

    public static async Task<AdmissionApplication> RequireOfferAsync(IAppDbContext db, Guid applicationId, CancellationToken cancellationToken)
    {
        var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        if (!await db.AdmissionOffers.AnyAsync(o => o.ApplicationId == applicationId, cancellationToken)) throw new DomainException("Record the school's offer first.");
        return application;
    }
}

// ─────────────────────────── PAL follow-up reminders ───────────────────────────

/// <summary>An application where the school's acceptance letter has arrived but the PAL hasn't, so staff need to chase it.</summary>
public record PalFollowUp(Guid ApplicationId, Guid CandidateId, string CandidateName, string InstitutionName, string ProgrammeName, DateOnly AcceptanceReceivedOn, int DaysWaiting, bool Overdue);

internal static class PalFollowUps
{
    public static async Task<List<PalFollowUp>> ListAsync(IAppDbContext db, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var inactive = new[] { AdmissionStatus.Rejected, AdmissionStatus.Withdrawn, AdmissionStatus.Closed };
        var today = DateOnly.FromDateTime(nowUtc);
        var accepted = await db.AdmissionAcceptances.AsNoTracking().Where(a => a.PalNumber == null).ToListAsync(cancellationToken);
        var ids = accepted.Select(a => a.ApplicationId).ToList();
        var rows = await AdmissionMapping.Rows(db, db.AdmissionApplications.AsNoTracking().Where(a => ids.Contains(a.Id) && !inactive.Contains(a.Status) && a.Country == DestinationCountry.Canada && a.Level == ApplicantLevel.Undergraduate)).ToListAsync(cancellationToken);
        var docs = await Outcomes.DocsAsync(db, rows.Select(r => r.Id).ToList(), cancellationToken);
        var result = new List<PalFollowUp>();
        foreach (var r in rows)
        {
            if (docs.ContainsKey((r.Id, DocumentPurpose.PalLetter))) continue;
            var received = accepted.First(a => a.ApplicationId == r.Id).ReceivedOn;
            var days = Math.Max(0, today.DayNumber - received.DayNumber);
            result.Add(new PalFollowUp(r.Id, r.CandidateId, r.CandidateName, r.InstitutionName, r.ProgrammeName, received, days, days >= PalRules.FollowUpAfterDays));
        }

        return result.OrderByDescending(x => x.DaysWaiting).ToList();
    }
}

public record ListPalFollowUpsQuery : IQuery<IReadOnlyList<PalFollowUp>>;

public class ListPalFollowUpsHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<ListPalFollowUpsQuery, IReadOnlyList<PalFollowUp>>
{
    public async ValueTask<IReadOnlyList<PalFollowUp>> Handle(ListPalFollowUpsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await PalFollowUps.ListAsync(db, clock.UtcNow, cancellationToken);
    }
}

public record GetOutcomeQuery(Guid ApplicationId) : IQuery<OutcomeResult>;

public class GetOutcomeHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetOutcomeQuery, OutcomeResult>
{
    public async ValueTask<OutcomeResult> Handle(GetOutcomeQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await Outcomes.ForAsync(db, query.ApplicationId, cancellationToken);
    }
}

// ─────────────────────────── The offer ───────────────────────────

public record RecordOfferCommand(Guid ApplicationId, string? Conditions, DateOnly? RespondByOn) : ICommand<OutcomeResult>;

public class RecordOfferValidator : AbstractValidator<RecordOfferCommand>
{
    public RecordOfferValidator() => RuleFor(x => x.Conditions).MaximumLength(2000);
}

public class RecordOfferHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IEmailSender email, IAppUrls urls, ILogger<RecordOfferHandler> logger) : ICommandHandler<RecordOfferCommand, OutcomeResult>
{
    public async ValueTask<OutcomeResult> Handle(RecordOfferCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == command.ApplicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        var offer = await db.AdmissionOffers.FirstOrDefaultAsync(o => o.ApplicationId == application.Id, cancellationToken);
        var isNew = offer is null;
        if (offer is null)
        {
            // The offer arriving is a status move first (Offer Received); this adds its details.
            if (application.Status != AdmissionStatus.OfferReceived) throw new DomainException("Record the application as Offer Received first, then add the offer's details.");
            offer = new AdmissionOffer(application.Id, command.Conditions, command.RespondByOn, current.Email);
            db.AdmissionOffers.Add(offer);
        }
        else
        {
            offer.Update(command.Conditions, command.RespondByOn);
        }

        audit.Add(AuditAction.OfferRecorded, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { change = isNew ? "recorded" : "updated" });
        await db.SaveChangesAsync(cancellationToken);

        if (isNew)
        {
            // The student is told there's something to look at: a link only, the offer itself is behind sign-in.
            var profile = await db.CandidateProfiles.AsNoTracking().FirstAsync(p => p.Id == application.CandidateId, cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.OfferAvailable(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/applications"), logger, cancellationToken);
        }

        return await Outcomes.ForAsync(db, application.Id, cancellationToken);
    }
}

// ─────────────────────────── The formal acceptance letter and the PAL ───────────────────────────

public record RecordAcceptanceCommand(Guid ApplicationId, DateOnly ReceivedOn, string? PalNumber, string? Note) : ICommand<OutcomeResult>;

public class RecordAcceptanceValidator : AbstractValidator<RecordAcceptanceCommand>
{
    public RecordAcceptanceValidator()
    {
        RuleFor(x => x.PalNumber).MaximumLength(60);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class RecordAcceptanceHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<RecordAcceptanceCommand, OutcomeResult>
{
    public async ValueTask<OutcomeResult> Handle(RecordAcceptanceCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var application = await Outcomes.RequireOfferAsync(db, command.ApplicationId, cancellationToken);
        if (command.ReceivedOn > DateOnly.FromDateTime(clock.UtcNow)) throw new DomainException("The date the letter arrived can't be in the future.");
        if (!string.IsNullOrWhiteSpace(command.PalNumber) && !PalRules.Applies(application.Country, application.Level))
        {
            throw new DomainException("A PAL number only goes with a Canadian undergraduate acceptance.");
        }

        var acceptance = await db.AdmissionAcceptances.FirstOrDefaultAsync(a => a.ApplicationId == application.Id, cancellationToken);
        var isNew = acceptance is null;
        if (acceptance is null)
        {
            acceptance = new AdmissionAcceptance(application.Id, command.ReceivedOn, command.PalNumber, command.Note, current.Email);
            db.AdmissionAcceptances.Add(acceptance);
        }
        else
        {
            acceptance.Update(command.ReceivedOn, command.PalNumber, command.Note);
        }

        audit.Add(AuditAction.AcceptanceRecorded, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { change = isNew ? "recorded" : "updated", pal = !string.IsNullOrWhiteSpace(command.PalNumber) });
        await db.SaveChangesAsync(cancellationToken);
        return await Outcomes.ForAsync(db, application.Id, cancellationToken);
    }
}
