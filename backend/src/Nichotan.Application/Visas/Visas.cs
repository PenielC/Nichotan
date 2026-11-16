using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Admissions;
using Nichotan.Application.Candidates;
using Nichotan.Application.Identity;
using Nichotan.Application.Common;
using Nichotan.Application.Documents;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Identity;
using Nichotan.Domain.Visas;

namespace Nichotan.Application.Visas;

// Visa cases. Each is tied to one accepted admission application, which supplies the country, school and programme.
// Statuses are entered by staff from what the authority tells them; nothing syncs with an immigration system.

public record VisaSummary(
    Guid Id,
    Guid ApplicationId,
    Guid CandidateId,
    string CandidateName,
    string InstitutionName,
    string ProgrammeName,
    DestinationCountry Country,
    VisaStatus Status,
    DateTime StatusChangedAtUtc,
    string? ReferenceNumber,
    DateOnly? SubmittedOn,
    DateOnly? BiometricsAppointmentOn,
    DateOnly? DecisionOn);

public record VisaEventResult(Guid Id, VisaStatus? From, VisaStatus To, DateOnly OccurredOn, DateTime RecordedAtUtc, string? RecordedBy, string? Note, bool IsCorrection);

public record VisaMove(VisaStatus Status, bool NoteRequired);

public record VisaDetail(
    VisaSummary Summary,
    string? BiometricsLocation,
    DateOnly OpenedOn,
    IReadOnlyList<VisaMove> AllowedMoves,
    IReadOnlyList<VisaEventResult> History,
    DocumentGaps DocumentGaps,
    DocRef? BiometricsLetter,
    DocRef? DecisionLetter);

/// <summary>What a candidate sees of their own visa case: no staff notes or names.</summary>
public record MyVisaResult(
    Guid Id,
    Guid ApplicationId,
    string InstitutionName,
    string ProgrammeName,
    DestinationCountry Country,
    VisaStatus Status,
    DateTime StatusChangedAtUtc,
    DateOnly? BiometricsAppointmentOn,
    string? BiometricsLocation,
    IReadOnlyList<MyVisaStep> History,
    int DocumentsMissing,
    bool CanUploadBiometricsLetter,
    MyFile? BiometricsLetter,
    Guid? BiometricsLetterTypeId,
    MyFile? DecisionLetter,
    bool CanMarkBiometricsDone);

public record MyVisaStep(VisaStatus Status, DateOnly OccurredOn);

internal static class VisaMapping
{
    public sealed class Row
    {
        public Guid Id { get; init; }
        public Guid ApplicationId { get; init; }
        public Guid CandidateId { get; init; }
        public string CandidateName { get; init; } = string.Empty;
        public string InstitutionName { get; init; } = string.Empty;
        public string ProgrammeName { get; init; } = string.Empty;
        public DestinationCountry Country { get; init; }
        public VisaStatus Status { get; init; }
        public DateTime StatusChangedAtUtc { get; init; }
        public string? ReferenceNumber { get; init; }
        public DateOnly? SubmittedOn { get; init; }
        public DateOnly? BiometricsAppointmentOn { get; init; }
        public string? BiometricsLocation { get; init; }
        public DateOnly? DecisionOn { get; init; }
        public DateOnly OpenedOn { get; init; }

        public VisaSummary ToSummary() => new(Id, ApplicationId, CandidateId, CandidateName, InstitutionName, ProgrammeName, Country, Status, StatusChangedAtUtc, ReferenceNumber, SubmittedOn, BiometricsAppointmentOn, DecisionOn);
    }

    public static IQueryable<Row> Rows(IAppDbContext db, IQueryable<VisaCase> cases) =>
        from v in cases
        join a in db.AdmissionApplications on v.ApplicationId equals a.Id
        join c in db.CandidateProfiles on v.CandidateId equals c.Id
        join i in db.Institutions on a.InstitutionId equals i.Id
        join p in db.Programmes on a.ProgrammeId equals p.Id
        select new Row
        {
            Id = v.Id,
            ApplicationId = v.ApplicationId,
            CandidateId = v.CandidateId,
            CandidateName = c.FirstName + " " + c.LastName,
            InstitutionName = i.Name,
            ProgrammeName = p.Name,
            Country = a.Country,
            Status = v.Status,
            StatusChangedAtUtc = v.StatusChangedAtUtc,
            ReferenceNumber = v.ReferenceNumber,
            SubmittedOn = v.SubmittedOn,
            BiometricsAppointmentOn = v.BiometricsAppointmentOn,
            BiometricsLocation = v.BiometricsLocation,
            DecisionOn = v.DecisionOn,
            OpenedOn = v.OpenedOn,
        };

    public static IReadOnlyList<VisaMove> MovesFrom(VisaStatus status) =>
        VisaWorkflow.NextFrom(status).Select(s => new VisaMove(s, VisaWorkflow.RequiresNote(s))).ToList();

    public static async Task<VisaDetail> DetailAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var row = await Rows(db, db.VisaCases.AsNoTracking().Where(v => v.Id == id)).FirstOrDefaultAsync(cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        var history = await db.VisaStatusEvents.AsNoTracking().Where(e => e.VisaCaseId == id).OrderBy(e => e.RecordedAtUtc).ToListAsync(cancellationToken);
        var gaps = (await Slots.ChecklistAsync(db, row.ApplicationId, ChecklistStage.Visa, cancellationToken)).Gaps;
        var docs = await Outcomes.DocsAsync(db, [row.ApplicationId], cancellationToken);
        return new VisaDetail(
            row.ToSummary(), row.BiometricsLocation, row.OpenedOn, MovesFrom(row.Status),
            history.Select(e => new VisaEventResult(e.Id, e.From, e.To, e.OccurredOn, e.RecordedAtUtc, e.RecordedByEmail, e.Note, e.IsCorrection)).ToList(),
            gaps, docs.GetValueOrDefault((row.ApplicationId, DocumentPurpose.BiometricsLetter)), docs.GetValueOrDefault((row.ApplicationId, DocumentPurpose.DecisionLetter)));
    }
}

public record ListVisasQuery(VisaStatus? Status, DestinationCountry? Country, Guid? ApplicationId, Guid? CandidateId, string? Search, int Page = 1, int PageSize = 25) : IQuery<PagedResult<VisaSummary>>;

public class ListVisasValidator : AbstractValidator<ListVisasQuery>
{
    public ListVisasValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class ListVisasHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListVisasQuery, PagedResult<VisaSummary>>
{
    public async ValueTask<PagedResult<VisaSummary>> Handle(ListVisasQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var cases = db.VisaCases.AsNoTracking().AsQueryable();
        if (query.Status is { } status) cases = cases.Where(v => v.Status == status);
        if (query.ApplicationId is { } applicationId) cases = cases.Where(v => v.ApplicationId == applicationId);
        if (query.CandidateId is { } candidateId) cases = cases.Where(v => v.CandidateId == candidateId);
        var rows = VisaMapping.Rows(db, cases);
        if (query.Country is { } country) rows = rows.Where(r => r.Country == country);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(r => r.CandidateName.ToLower().Contains(term) || r.InstitutionName.ToLower().Contains(term));
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(r => r.StatusChangedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<VisaSummary>(items.Select(r => r.ToSummary()).ToList(), query.Page, query.PageSize, total);
    }
}

public record GetVisaQuery(Guid Id) : IQuery<VisaDetail>;

public class GetVisaHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetVisaQuery, VisaDetail>
{
    public async ValueTask<VisaDetail> Handle(GetVisaQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await VisaMapping.DetailAsync(db, query.Id, cancellationToken);
    }
}

/// <summary>Opens the visa case for an admission application whose formal acceptance has been recorded.</summary>
public record OpenVisaCaseCommand(Guid ApplicationId) : ICommand<VisaDetail>;

public class OpenVisaCaseHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<OpenVisaCaseCommand, VisaDetail>
{
    public async ValueTask<VisaDetail> Handle(OpenVisaCaseCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.VisaStaff);
        var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == command.ApplicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        if (!AdmissionWorkflow.IsActive(application.Status)) throw new DomainException("This application is finished, so a visa case can't be opened for it.");
        if (!await db.AdmissionAcceptances.AnyAsync(a => a.ApplicationId == application.Id, cancellationToken)) throw new DomainException("Record the school's formal acceptance before opening a visa case.");
        if (await db.VisaCases.AnyAsync(v => v.ApplicationId == application.Id, cancellationToken)) throw new DomainException("A visa case has already been opened for this application.");

        var visa = new VisaCase(application.Id, application.CandidateId, AdmissionMapping.Today(clock), clock.UtcNow);
        db.VisaCases.Add(visa);
        db.VisaStatusEvents.Add(visa.RecordOpened(userId, current.Email, clock.UtcNow));
        // The visa checklist is copied from the template now, so later template edits don't rewrite this case.
        await Slots.SnapshotAsync(db, await db.AdmissionApplications.FirstAsync(a => a.Id == application.Id, cancellationToken), cancellationToken, ChecklistStage.Visa);
        audit.Add(AuditAction.VisaCaseOpened, entityType: nameof(VisaCase), entityId: visa.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await VisaMapping.DetailAsync(db, visa.Id, cancellationToken);
    }
}

/// <summary>Records a move that staff made, or learned of, with the immigration authority.</summary>
public record ChangeVisaStatusCommand(Guid Id, VisaStatus ToStatus, DateOnly? OccurredOn, string? Note, string? ReferenceNumber, DateOnly? BiometricsAppointmentOn, string? BiometricsLocation, bool AcknowledgeDocumentGaps = false) : ICommand<VisaDetail>;

public class ChangeVisaStatusValidator : AbstractValidator<ChangeVisaStatusCommand>
{
    public ChangeVisaStatusValidator()
    {
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.BiometricsLocation).MaximumLength(300);
    }
}

public class ChangeVisaStatusHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock, IEmailSender email, IAppUrls urls, ILogger<ChangeVisaStatusHandler> logger)
    : ICommandHandler<ChangeVisaStatusCommand, VisaDetail>
{
    public async ValueTask<VisaDetail> Handle(ChangeVisaStatusCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.VisaStaff);
        var visa = await db.VisaCases.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        // Documents: staff may lodge the visa application without them, but only knowingly, and the history says so.
        var note = command.Note;
        if (command.ToStatus == VisaStatus.Submitted && VisaWorkflow.CanMove(visa.Status, VisaStatus.Submitted) && visa.Status == VisaStatus.Preparing)
        {
            var outstanding = (await Slots.ChecklistAsync(db, visa.ApplicationId, ChecklistStage.Visa, cancellationToken)).Gaps.NotAccepted;
            if (outstanding > 0)
            {
                if (!command.AcknowledgeDocumentGaps) throw new DomainException($"{outstanding} required visa document{(outstanding == 1 ? " is" : "s are")} not yet uploaded. Confirm you want to continue without {(outstanding == 1 ? "it" : "them")}.");
                note = string.IsNullOrWhiteSpace(note) ? "Proceeded with required documents outstanding." : $"{note.Trim()} (Proceeded with required documents outstanding.)";
            }
        }

        var today = AdmissionMapping.Today(clock);
        var from = visa.Status;
        var recorded = visa.MoveTo(command.ToStatus, command.OccurredOn ?? today, note, userId, current.Email, today, clock.UtcNow);
        if (command.ReferenceNumber is not null || command.BiometricsAppointmentOn is not null || command.BiometricsLocation is not null)
        {
            visa.UpdateDetails(command.ReferenceNumber ?? visa.ReferenceNumber, command.BiometricsAppointmentOn ?? visa.BiometricsAppointmentOn, command.BiometricsLocation ?? visa.BiometricsLocation);
        }

        db.VisaStatusEvents.Add(recorded);
        audit.Add(AuditAction.VisaStatusChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { from = from.ToString(), to = command.ToStatus.ToString() });
        await db.SaveChangesAsync(cancellationToken);

        // Submitting starts the biometrics reminder: the student is told what comes next (and staff see it on the dashboard).
        if (command.ToStatus == VisaStatus.Submitted && from == VisaStatus.Preparing)
        {
            var profile = await db.CandidateProfiles.AsNoTracking().FirstAsync(p => p.Id == visa.CandidateId, cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.VisaSubmitted(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/applications"), logger, cancellationToken);
        }

        return await VisaMapping.DetailAsync(db, visa.Id, cancellationToken);
    }
}

/// <summary>Fixes a wrongly recorded status. Administrators only; the original entry stays in the history.</summary>
public record CorrectVisaStatusCommand(Guid Id, VisaStatus ToStatus, DateOnly? OccurredOn, string Note) : ICommand<VisaDetail>;

public class CorrectVisaStatusValidator : AbstractValidator<CorrectVisaStatusCommand>
{
    public CorrectVisaStatusValidator() => RuleFor(x => x.Note).NotEmpty().MaximumLength(2000);
}

public class CorrectVisaStatusHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<CorrectVisaStatusCommand, VisaDetail>
{
    public async ValueTask<VisaDetail> Handle(CorrectVisaStatusCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Administrator);
        var visa = await db.VisaCases.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        var today = AdmissionMapping.Today(clock);
        var from = visa.Status;
        db.VisaStatusEvents.Add(visa.Correct(command.ToStatus, command.OccurredOn ?? today, command.Note, userId, current.Email, today, clock.UtcNow));
        audit.Add(AuditAction.VisaStatusCorrected, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { from = from.ToString(), to = command.ToStatus.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return await VisaMapping.DetailAsync(db, visa.Id, cancellationToken);
    }
}

public record UpdateVisaDetailsCommand(Guid Id, string? ReferenceNumber, DateOnly? BiometricsAppointmentOn, string? BiometricsLocation) : ICommand<VisaDetail>;

public class UpdateVisaDetailsValidator : AbstractValidator<UpdateVisaDetailsCommand>
{
    public UpdateVisaDetailsValidator()
    {
        RuleFor(x => x.ReferenceNumber).MaximumLength(100);
        RuleFor(x => x.BiometricsLocation).MaximumLength(300);
    }
}

public class UpdateVisaDetailsHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<UpdateVisaDetailsCommand, VisaDetail>
{
    public async ValueTask<VisaDetail> Handle(UpdateVisaDetailsCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var visa = await db.VisaCases.FirstOrDefaultAsync(v => v.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        visa.UpdateDetails(command.ReferenceNumber, command.BiometricsAppointmentOn, command.BiometricsLocation);
        audit.Add(AuditAction.VisaDetailsUpdated, entityType: nameof(VisaCase), entityId: visa.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await VisaMapping.DetailAsync(db, visa.Id, cancellationToken);
    }
}

// ─────────────────────────── The visa checklist ───────────────────────────

internal static class VisaChecklist
{
    public static async Task<(VisaCase Visa, AdmissionApplication Application)> LoadAsync(IAppDbContext db, Guid visaCaseId, bool forChange, CancellationToken cancellationToken)
    {
        var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visaCaseId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        if (forChange && VisaWorkflow.IsFinished(visa.Status)) throw new DomainException("This visa case is finished, so its checklist can't change.");
        var application = await db.AdmissionApplications.AsNoTracking().FirstAsync(a => a.Id == visa.ApplicationId, cancellationToken);
        return (visa, application);
    }
}

public record GetVisaChecklistQuery(Guid VisaCaseId) : IQuery<ChecklistResult>;

public class GetVisaChecklistHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetVisaChecklistQuery, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(GetVisaChecklistQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var (_, application) = await VisaChecklist.LoadAsync(db, query.VisaCaseId, forChange: false, cancellationToken);
        return await Slots.ChecklistAsync(db, application.Id, ChecklistStage.Visa, cancellationToken);
    }
}

/// <summary>Brings a visa case's checklist in line with the current visa template. Items staff added or waived by hand are left alone.</summary>
public record RefreshVisaChecklistCommand(Guid VisaCaseId) : ICommand<ChecklistResult>;

public class RefreshVisaChecklistHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<RefreshVisaChecklistCommand, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(RefreshVisaChecklistCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var (visa, application) = await VisaChecklist.LoadAsync(db, command.VisaCaseId, forChange: true, cancellationToken);
        var template = (await Slots.ResolveTemplateAsync(db, application, ChecklistStage.Visa, cancellationToken)).Where(t => t.IsRequired).Select(t => t.DocumentTypeId).ToHashSet();
        var items = await db.ChecklistItems.Where(i => i.ApplicationId == application.Id && i.Stage == ChecklistStage.Visa).ToListAsync(cancellationToken);
        foreach (var typeId in template)
        {
            var item = items.FirstOrDefault(i => i.DocumentTypeId == typeId);
            if (item is null) db.ChecklistItems.Add(new ApplicationChecklistItem(application.Id, typeId, true, ChecklistSource.Template, null, ChecklistStage.Visa));
            else if (item.Source == ChecklistSource.Template) item.Set(true, ChecklistSource.Template, null);
        }

        foreach (var item in items.Where(i => i.Source == ChecklistSource.Template && !template.Contains(i.DocumentTypeId))) item.Set(false, ChecklistSource.Template, null);
        audit.Add(AuditAction.ChecklistChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { change = "refreshed" });
        await db.SaveChangesAsync(cancellationToken);
        return await Slots.ChecklistAsync(db, application.Id, ChecklistStage.Visa, cancellationToken);
    }
}

/// <summary>Adds a document to one visa case's checklist, makes it required or optional, or waives it. Waiving needs a note.</summary>
public record SetVisaChecklistItemCommand(Guid VisaCaseId, Guid DocumentTypeId, bool Required, string? Note) : ICommand<ChecklistResult>;

public class SetVisaChecklistItemValidator : AbstractValidator<SetVisaChecklistItemCommand>
{
    public SetVisaChecklistItemValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public class SetVisaChecklistItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SetVisaChecklistItemCommand, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(SetVisaChecklistItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.VisaStaff);
        var (visa, application) = await VisaChecklist.LoadAsync(db, command.VisaCaseId, forChange: true, cancellationToken);
        var type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.DocumentTypeId && t.IsActive, cancellationToken) ?? throw new NotFoundException("Document type not found.");
        if (type.Purpose != DocumentPurpose.None || type.IsAutomatic) throw new DomainException("The system manages this document, so it isn't part of the checklist.");
        var item = await db.ChecklistItems.FirstOrDefaultAsync(i => i.ApplicationId == application.Id && i.Stage == ChecklistStage.Visa && i.DocumentTypeId == type.Id, cancellationToken);
        if (item is { IsRequired: true } && !command.Required && string.IsNullOrWhiteSpace(command.Note)) throw new DomainException("Say why this document is being waived.");

        if (item is null) db.ChecklistItems.Add(new ApplicationChecklistItem(application.Id, type.Id, command.Required, ChecklistSource.Manual, command.Note, ChecklistStage.Visa));
        else item.Set(command.Required, ChecklistSource.Manual, command.Note);
        audit.Add(AuditAction.ChecklistChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { change = command.Required ? "required" : "waived", type = type.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await Slots.ChecklistAsync(db, application.Id, ChecklistStage.Visa, cancellationToken);
    }
}

// ─────────────────────────── Biometrics: the reminder, and saying they're done ───────────────────────────

/// <summary>A visa case that has been lodged but whose biometrics aren't marked done yet.</summary>
public record BiometricsFollowUp(Guid VisaCaseId, Guid ApplicationId, Guid CandidateId, string CandidateName, string InstitutionName, VisaStatus Status, DateOnly? SubmittedOn, DateOnly? AppointmentOn, int DaysWaiting, bool Overdue);

public record ListBiometricsFollowUpsQuery : IQuery<IReadOnlyList<BiometricsFollowUp>>;

internal static class BiometricsFollowUps
{
    public static async Task<List<BiometricsFollowUp>> ListAsync(IAppDbContext db, DateOnly today, CancellationToken cancellationToken)
    {
        var waiting = new[] { VisaStatus.Submitted, VisaStatus.BiometricsRequested };
        var rows = await VisaMapping.Rows(db, db.VisaCases.AsNoTracking().Where(v => waiting.Contains(v.Status))).ToListAsync(cancellationToken);
        return rows.Select(r =>
        {
            var since = r.SubmittedOn ?? DateOnly.FromDateTime(r.StatusChangedAtUtc);
            var days = Math.Max(0, today.DayNumber - since.DayNumber);
            // Overdue once the appointment has passed with nothing marked done, or the wait is long.
            var overdue = days >= VisaRules.BiometricsFollowUpAfterDays || (r.BiometricsAppointmentOn is { } appointment && appointment < today);
            return new BiometricsFollowUp(r.Id, r.ApplicationId, r.CandidateId, r.CandidateName, r.InstitutionName, r.Status, r.SubmittedOn, r.BiometricsAppointmentOn, days, overdue);
        }).OrderByDescending(f => f.Overdue).ThenByDescending(f => f.DaysWaiting).ToList();
    }
}

public class ListBiometricsFollowUpsHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<ListBiometricsFollowUpsQuery, IReadOnlyList<BiometricsFollowUp>>
{
    public async ValueTask<IReadOnlyList<BiometricsFollowUp>> Handle(ListBiometricsFollowUpsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await BiometricsFollowUps.ListAsync(db, AdmissionMapping.Today(clock), cancellationToken);
    }
}

/// <summary>The student says their biometrics have been done. Staff are told so they can check it and keep the record right.</summary>
public record MarkMyBiometricsDoneCommand(Guid VisaCaseId, DateOnly? DoneOn) : ICommand<MyVisaStep>;

public class MarkMyBiometricsDoneHandler(
    IAppDbContext db,
    ICurrentUser current,
    IAuditLog audit,
    IClock clock,
    IEmailSender email,
    IAppUrls urls,
    IOptions<AdmissionNotificationOptions> notify,
    ILogger<MarkMyBiometricsDoneHandler> logger) : ICommandHandler<MarkMyBiometricsDoneCommand, MyVisaStep>
{
    public async ValueTask<MyVisaStep> Handle(MarkMyBiometricsDoneCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var visa = await db.VisaCases.FirstOrDefaultAsync(v => v.Id == command.VisaCaseId && v.CandidateId == candidateId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
        if (!VisaWorkflow.AwaitingBiometrics(visa.Status)) throw new DomainException("Your biometrics can be marked as done once the visa application has been submitted.");
        var today = AdmissionMapping.Today(clock);
        var recorded = visa.MoveTo(VisaStatus.BiometricsCompleted, command.DoneOn ?? today, "Marked as done by the student.", userId, current.Email, today, clock.UtcNow);
        db.VisaStatusEvents.Add(recorded);
        audit.Add(AuditAction.VisaStatusChanged, entityType: nameof(VisaCase), entityId: visa.Id.ToString(), details: new { from = recorded.From?.ToString(), to = recorded.To.ToString(), by = "student" });
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(notify.Value.NotifyEmail))
        {
            var name = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == candidateId).Select(p => p.FirstName + " " + p.LastName).FirstAsync(cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.BiometricsDone(notify.Value.NotifyEmail.Trim(), name, $"{urls.WebBaseUrl}/staff/visa/{visa.Id}"), logger, cancellationToken);
        }

        return new MyVisaStep(recorded.To, recorded.OccurredOn);
    }
}

/// <summary>Current pipeline counts (cases in these states now), not movement in a month.</summary>
public record VisaStats(int InProcess, int Approved, int Preparing);

public record GetVisaStatsQuery : IQuery<VisaStats>;

public class GetVisaStatsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetVisaStatsQuery, VisaStats>
{
    public async ValueTask<VisaStats> Handle(GetVisaStatsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var inProcess = new[] { VisaStatus.Submitted, VisaStatus.BiometricsRequested, VisaStatus.BiometricsCompleted, VisaStatus.AdditionalInformationRequested };
        var cases = db.VisaCases.AsNoTracking();
        return new VisaStats(
            await cases.CountAsync(v => inProcess.Contains(v.Status), cancellationToken),
            await cases.CountAsync(v => v.Status == VisaStatus.Approved, cancellationToken),
            await cases.CountAsync(v => v.Status == VisaStatus.Preparing, cancellationToken));
    }
}

public record GetMyVisasQuery : IQuery<IReadOnlyList<MyVisaResult>>;

public class GetMyVisasHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyVisasQuery, IReadOnlyList<MyVisaResult>>
{
    public async ValueTask<IReadOnlyList<MyVisaResult>> Handle(GetMyVisasQuery query, CancellationToken cancellationToken)
    {
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var rows = await VisaMapping.Rows(db, db.VisaCases.AsNoTracking().Where(v => v.CandidateId == candidateId)).OrderByDescending(r => r.StatusChangedAtUtc).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var events = await db.VisaStatusEvents.AsNoTracking().Where(e => ids.Contains(e.VisaCaseId)).OrderBy(e => e.RecordedAtUtc).ToListAsync(cancellationToken);
        var docs = await Outcomes.DocsAsync(db, rows.Select(r => r.ApplicationId).ToList(), cancellationToken);
        // Which kind of document the biometrics letter is, so the student's upload can name it (they can't read the staff list).
        Guid? letterTypeId = await db.DocumentTypes.AsNoTracking().Where(t => t.Purpose == DocumentPurpose.BiometricsLetter).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);
        var results = new List<MyVisaResult>();
        foreach (var r in rows)
        {
            var missing = VisaWorkflow.IsFinished(r.Status) ? 0 : (await Slots.ChecklistAsync(db, r.ApplicationId, ChecklistStage.Visa, cancellationToken)).Gaps.Missing;
            MyFile? File(DocumentPurpose purpose) => docs.GetValueOrDefault((r.ApplicationId, purpose)) is { } d ? new MyFile(d.DocumentId, d.FileName) : null;
            results.Add(new MyVisaResult(
                r.Id, r.ApplicationId, r.InstitutionName, r.ProgrammeName, r.Country, r.Status, r.StatusChangedAtUtc, r.BiometricsAppointmentOn, r.BiometricsLocation,
                events.Where(e => e.VisaCaseId == r.Id && !e.IsCorrection).Select(e => new MyVisaStep(e.To, e.OccurredOn)).ToList(),
                missing, VisaWorkflow.AcceptsBiometricsLetter(r.Status), File(DocumentPurpose.BiometricsLetter), letterTypeId, File(DocumentPurpose.DecisionLetter), VisaWorkflow.AwaitingBiometrics(r.Status)));
        }

        return results;
    }
}
