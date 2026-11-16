using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Application.Documents;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Admissions;

// ─────────────────────────── Results ───────────────────────────

public record IntakeDto(StartTerm Term, int Year);

public record InstitutionResult(Guid Id, string Name, DestinationCountry Country, string? Website, string? Notes, bool IsArchived, int ProgrammeCount);

public record ProgrammeResult(Guid Id, Guid InstitutionId, string InstitutionName, DestinationCountry Country, string Name, ApplicantLevel Level, bool IsArchived, IReadOnlyList<IntakeDto> Intakes);

public record AdmissionSummary(
    Guid Id,
    Guid CandidateId,
    string CandidateName,
    string InstitutionName,
    string ProgrammeName,
    DestinationCountry Country,
    ApplicantLevel Level,
    StartTerm IntakeTerm,
    int IntakeYear,
    AdmissionStatus Status,
    DateTime StatusChangedAtUtc,
    string? InstitutionReference,
    DateOnly? SubmittedToInstitutionOn);

public record AdmissionEventResult(Guid Id, AdmissionStatus? From, AdmissionStatus To, DateOnly OccurredOn, DateTime RecordedAtUtc, string? RecordedBy, string? Note, bool IsCorrection);

public record AllowedMove(AdmissionStatus Status, bool NoteRequired);

public record AdmissionDetail(AdmissionSummary Summary, string? PortalUrl, bool FormSubmitted, IReadOnlyList<AllowedMove> AllowedMoves, IReadOnlyList<AdmissionEventResult> History, DocumentGaps DocumentGaps);

/// <summary>What a candidate sees of their own application: no staff notes or names.</summary>
public record MyAdmissionResult(
    Guid Id,
    string InstitutionName,
    string ProgrammeName,
    DestinationCountry Country,
    StartTerm IntakeTerm,
    int IntakeYear,
    AdmissionStatus Status,
    DateTime StatusChangedAtUtc,
    DateOnly? SubmittedToInstitutionOn,
    IReadOnlyList<MyAdmissionStep> History,
    bool CanSend,
    bool CanWithdraw,
    IReadOnlyList<string> Blockers,
    MyOffer? Offer,
    MyAcceptance? Acceptance,
    bool CanUploadOfferLetter,
    MyFile? OfferLetter,
    Guid? OfferLetterTypeId);

public record MyFile(Guid DocumentId, string FileName);

public record MyOffer(string? Conditions, DateOnly? RespondByOn, Guid? LetterDocumentId, string? LetterFileName);

public record MyAcceptance(DateOnly ReceivedOn, string? PalNumber, Guid? LetterDocumentId, Guid? PalLetterDocumentId);

public record MyAdmissionStep(AdmissionStatus Status, DateOnly OccurredOn);

internal static class AdmissionMapping
{
    /// <summary>The joined row, as a plain class: EF can sort and filter on its members, which it can't on a positional record.</summary>
    public sealed class Row
    {
        public Guid Id { get; init; }
        public Guid CandidateId { get; init; }
        public string CandidateName { get; init; } = string.Empty;
        public string InstitutionName { get; init; } = string.Empty;
        public string ProgrammeName { get; init; } = string.Empty;
        public DestinationCountry Country { get; init; }
        public ApplicantLevel Level { get; init; }
        public StartTerm IntakeTerm { get; init; }
        public int IntakeYear { get; init; }
        public AdmissionStatus Status { get; init; }
        public DateTime StatusChangedAtUtc { get; init; }
        public string? InstitutionReference { get; init; }
        public DateOnly? SubmittedToInstitutionOn { get; init; }

        public AdmissionSummary ToSummary() =>
            new(Id, CandidateId, CandidateName, InstitutionName, ProgrammeName, Country, Level, IntakeTerm, IntakeYear, Status, StatusChangedAtUtc, InstitutionReference, SubmittedToInstitutionOn);
    }

    public static IQueryable<Row> Rows(IAppDbContext db, IQueryable<AdmissionApplication> applications) =>
        from a in applications
        join c in db.CandidateProfiles on a.CandidateId equals c.Id
        join i in db.Institutions on a.InstitutionId equals i.Id
        join p in db.Programmes on a.ProgrammeId equals p.Id
        select new Row
        {
            Id = a.Id,
            CandidateId = a.CandidateId,
            CandidateName = c.FirstName + " " + c.LastName,
            InstitutionName = i.Name,
            ProgrammeName = p.Name,
            Country = a.Country,
            Level = a.Level,
            IntakeTerm = a.IntakeTerm,
            IntakeYear = a.IntakeYear,
            Status = a.Status,
            StatusChangedAtUtc = a.StatusChangedAtUtc,
            InstitutionReference = a.InstitutionReference,
            SubmittedToInstitutionOn = a.SubmittedToInstitutionOn,
        };

    public static AdmissionEventResult ToResult(AdmissionStatusEvent e) =>
        new(e.Id, e.From, e.To, e.OccurredOn, e.RecordedAtUtc, e.RecordedByEmail, e.Note, e.IsCorrection);

    public static IReadOnlyList<AllowedMove> MovesFrom(AdmissionStatus status) =>
        AdmissionWorkflow.NextFrom(status).Select(s => new AllowedMove(s, AdmissionWorkflow.RequiresNote(s))).ToList();

    public static DateOnly Today(IClock clock) => DateOnly.FromDateTime(clock.UtcNow);

    public static async Task<AdmissionDetail> DetailAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var summary = (await Rows(db, db.AdmissionApplications.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(cancellationToken))?.ToSummary()
            ?? throw new NotFoundException("Application not found.");
        var extra = await db.AdmissionApplications.AsNoTracking().Where(a => a.Id == id).Select(a => a.PortalUrl).FirstAsync(cancellationToken);
        var formSubmitted = await db.ApplicationForms.AsNoTracking().AnyAsync(f => f.CandidateId == summary.CandidateId && f.Status == FormStatus.Submitted, cancellationToken);
        var history = await db.AdmissionStatusEvents.AsNoTracking().Where(e => e.ApplicationId == id).OrderBy(e => e.RecordedAtUtc).ToListAsync(cancellationToken);
        var checklist = await Slots.ChecklistAsync(db, id, cancellationToken);
        return new AdmissionDetail(summary, extra, formSubmitted, MovesFrom(summary.Status), history.Select(ToResult).ToList(), checklist.Gaps);
    }
}

public static class UrlRule
{
    public static IRuleBuilderOptions<T, string?> OptionalWebAddress<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(500).Must(v => string.IsNullOrWhiteSpace(v) || (Uri.TryCreate(v.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            .WithMessage("Enter a full web address starting with https://");
}

// ─────────────────────────── Institutions ───────────────────────────

public record ListInstitutionsQuery(DestinationCountry? Country, string? Search, bool IncludeArchived) : IQuery<IReadOnlyList<InstitutionResult>>;

public class ListInstitutionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListInstitutionsQuery, IReadOnlyList<InstitutionResult>>
{
    public async ValueTask<IReadOnlyList<InstitutionResult>> Handle(ListInstitutionsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var institutions = db.Institutions.AsNoTracking().AsQueryable();
        if (query.Country is { } country) institutions = institutions.Where(i => i.Country == country);
        if (!query.IncludeArchived) institutions = institutions.Where(i => !i.IsArchived);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            institutions = institutions.Where(i => i.Name.ToLower().Contains(term));
        }

        return await institutions.OrderBy(i => i.Country).ThenBy(i => i.Name)
            .Select(i => new InstitutionResult(i.Id, i.Name, i.Country, i.Website, i.Notes, i.IsArchived, db.Programmes.Count(p => p.InstitutionId == i.Id && !p.IsArchived)))
            .ToListAsync(cancellationToken);
    }
}

public record SaveInstitutionCommand(Guid? Id, string Name, DestinationCountry Country, string? Website, string? Notes) : ICommand<InstitutionResult>;

public class SaveInstitutionValidator : AbstractValidator<SaveInstitutionCommand>
{
    public SaveInstitutionValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Website).OptionalWebAddress();
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class SaveInstitutionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SaveInstitutionCommand, InstitutionResult>
{
    public async ValueTask<InstitutionResult> Handle(SaveInstitutionCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var name = command.Name.Trim();
        Institution institution;
        if (command.Id is { } id)
        {
            institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == id, cancellationToken) ?? throw new NotFoundException("Institution not found.");
            if (institution.Country != command.Country && await db.AdmissionApplications.AnyAsync(a => a.InstitutionId == id, cancellationToken))
            {
                throw new DomainException("Applications already use this institution, so its country can't change.");
            }

            if (await db.Institutions.AnyAsync(i => i.Id != id && i.Country == command.Country && i.Name.ToLower() == name.ToLower(), cancellationToken)) throw new ConflictException("That institution is already listed.");
            institution.Update(command.Name, command.Website, command.Notes);
            audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Institution), entityId: id.ToString(), details: new { change = "updated" });
        }
        else
        {
            if (await db.Institutions.AnyAsync(i => i.Country == command.Country && i.Name.ToLower() == name.ToLower(), cancellationToken)) throw new ConflictException("That institution is already listed.");
            institution = new Institution(command.Name, command.Country, command.Website, command.Notes);
            db.Institutions.Add(institution);
            audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Institution), entityId: institution.Id.ToString(), details: new { change = "created" });
        }

        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Programmes.CountAsync(p => p.InstitutionId == institution.Id && !p.IsArchived, cancellationToken);
        return new InstitutionResult(institution.Id, institution.Name, institution.Country, institution.Website, institution.Notes, institution.IsArchived, count);
    }
}

public record SetInstitutionArchivedCommand(Guid Id, bool Archived) : ICommand<InstitutionResult>;

public class SetInstitutionArchivedHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SetInstitutionArchivedCommand, InstitutionResult>
{
    public async ValueTask<InstitutionResult> Handle(SetInstitutionArchivedCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Institution not found.");
        institution.SetArchived(command.Archived);
        audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Institution), entityId: institution.Id.ToString(), details: new { change = command.Archived ? "archived" : "restored" });
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.Programmes.CountAsync(p => p.InstitutionId == institution.Id && !p.IsArchived, cancellationToken);
        return new InstitutionResult(institution.Id, institution.Name, institution.Country, institution.Website, institution.Notes, institution.IsArchived, count);
    }
}

// ─────────────────────────── Programmes ───────────────────────────

public record ListProgrammesQuery(Guid? InstitutionId, ApplicantLevel? Level, DestinationCountry? Country, bool IncludeArchived) : IQuery<IReadOnlyList<ProgrammeResult>>;

public class ListProgrammesHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListProgrammesQuery, IReadOnlyList<ProgrammeResult>>
{
    public async ValueTask<IReadOnlyList<ProgrammeResult>> Handle(ListProgrammesQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var rows = from p in db.Programmes.AsNoTracking()
                   join i in db.Institutions.AsNoTracking() on p.InstitutionId equals i.Id
                   select new { Programme = p, Institution = i };
        if (query.InstitutionId is { } institutionId) rows = rows.Where(r => r.Programme.InstitutionId == institutionId);
        if (query.Level is { } level) rows = rows.Where(r => r.Programme.Level == level);
        if (query.Country is { } country) rows = rows.Where(r => r.Institution.Country == country);
        if (!query.IncludeArchived) rows = rows.Where(r => !r.Programme.IsArchived && !r.Institution.IsArchived);

        var list = await rows.OrderBy(r => r.Institution.Name).ThenBy(r => r.Programme.Name).ToListAsync(cancellationToken);
        return list.Select(r => ToResult(r.Programme, r.Institution)).ToList();
    }

    internal static ProgrammeResult ToResult(Programme p, Institution i) =>
        new(p.Id, i.Id, i.Name, i.Country, p.Name, p.Level, p.IsArchived, p.Intakes.Select(x => new IntakeDto(x.Term, x.Year)).ToList());
}

public record SaveProgrammeCommand(Guid? Id, Guid InstitutionId, string Name, ApplicantLevel Level, IReadOnlyList<IntakeDto> Intakes) : ICommand<ProgrammeResult>;

public class SaveProgrammeValidator : AbstractValidator<SaveProgrammeCommand>
{
    public SaveProgrammeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Intakes).NotNull().Must(l => l is { Count: <= 20 }).WithMessage("List at most 20 intakes.");
        RuleForEach(x => x.Intakes).Must(i => i.Year is >= 2000 and <= 2100).WithMessage("Enter a valid year.");
    }
}

public class SaveProgrammeHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SaveProgrammeCommand, ProgrammeResult>
{
    public async ValueTask<ProgrammeResult> Handle(SaveProgrammeCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var institution = await db.Institutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == command.InstitutionId, cancellationToken) ?? throw new NotFoundException("Institution not found.");
        var name = command.Name.Trim().ToLower();
        var intakes = command.Intakes.Select(i => new ProgrammeIntake(i.Term, i.Year)).ToList();

        Programme programme;
        if (command.Id is { } id)
        {
            programme = await db.Programmes.FirstOrDefaultAsync(p => p.Id == id && p.InstitutionId == command.InstitutionId, cancellationToken) ?? throw new NotFoundException("Programme not found.");
            if (programme.Level != command.Level && await db.AdmissionApplications.AnyAsync(a => a.ProgrammeId == id, cancellationToken))
            {
                throw new DomainException("Applications already use this programme, so its level can't change.");
            }

            if (await db.Programmes.AnyAsync(p => p.Id != id && p.InstitutionId == command.InstitutionId && p.Level == command.Level && p.Name.ToLower() == name, cancellationToken)) throw new ConflictException("That programme is already listed for this institution.");
            programme.Update(command.Name, command.Level, intakes);
            audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Programme), entityId: id.ToString(), details: new { change = "updated" });
        }
        else
        {
            if (await db.Programmes.AnyAsync(p => p.InstitutionId == command.InstitutionId && p.Level == command.Level && p.Name.ToLower() == name, cancellationToken)) throw new ConflictException("That programme is already listed for this institution.");
            programme = new Programme(command.InstitutionId, command.Name, command.Level, intakes);
            db.Programmes.Add(programme);
            audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Programme), entityId: programme.Id.ToString(), details: new { change = "created" });
        }

        await db.SaveChangesAsync(cancellationToken);
        return ListProgrammesHandler.ToResult(programme, institution);
    }
}

public record SetProgrammeArchivedCommand(Guid Id, bool Archived) : ICommand<ProgrammeResult>;

public class SetProgrammeArchivedHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SetProgrammeArchivedCommand, ProgrammeResult>
{
    public async ValueTask<ProgrammeResult> Handle(SetProgrammeArchivedCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var programme = await db.Programmes.FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Programme not found.");
        var institution = await db.Institutions.AsNoTracking().FirstAsync(i => i.Id == programme.InstitutionId, cancellationToken);
        programme.SetArchived(command.Archived);
        audit.Add(AuditAction.DirectoryChanged, entityType: nameof(Programme), entityId: programme.Id.ToString(), details: new { change = command.Archived ? "archived" : "restored" });
        await db.SaveChangesAsync(cancellationToken);
        return ListProgrammesHandler.ToResult(programme, institution);
    }
}

// ─────────────────────────── Admission applications ───────────────────────────

public record CreateAdmissionCommand(Guid CandidateId, Guid ProgrammeId, StartTerm IntakeTerm, int IntakeYear) : ICommand<AdmissionDetail>;

public class CreateAdmissionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<CreateAdmissionCommand, AdmissionDetail>
{
    public async ValueTask<AdmissionDetail> Handle(CreateAdmissionCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.AdmissionsStaff);
        if (!await db.CandidateProfiles.AnyAsync(c => c.Id == command.CandidateId, cancellationToken)) throw new NotFoundException("Candidate not found.");
        var id = await AdmissionCreator.CreateAsync(db, audit, clock, userId, current.Email, command.CandidateId, command.ProgrammeId, command.IntakeTerm, command.IntakeYear, cancellationToken);
        return await AdmissionMapping.DetailAsync(db, id, cancellationToken);
    }
}

/// <summary>Creating an application, shared by staff (for a candidate) and by students (for themselves).</summary>
internal static class AdmissionCreator
{
    public static async Task<Guid> CreateAsync(IAppDbContext db, IAuditLog audit, IClock clock, Guid userId, string? userEmail, Guid candidateId, Guid programmeId, StartTerm term, int year, CancellationToken cancellationToken)
    {
        var programme = await db.Programmes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == programmeId, cancellationToken) ?? throw new NotFoundException("Programme not found.");
        var institution = await db.Institutions.AsNoTracking().FirstAsync(i => i.Id == programme.InstitutionId, cancellationToken);

        // The same programme and intake can't be open twice for one candidate; a finished one doesn't block a fresh start.
        var inactive = new[] { AdmissionStatus.Rejected, AdmissionStatus.Withdrawn, AdmissionStatus.Closed };
        if (await db.AdmissionApplications.AnyAsync(
                a => a.CandidateId == candidateId && a.ProgrammeId == programmeId && a.IntakeTerm == term && a.IntakeYear == year && !inactive.Contains(a.Status),
                cancellationToken))
        {
            throw new ConflictException("There is already an open application for that programme and intake.");
        }

        var now = clock.UtcNow;
        var application = new AdmissionApplication(candidateId, institution, programme, term, year, now);
        db.AdmissionApplications.Add(application);
        await Slots.SnapshotAsync(db, application, cancellationToken);
        db.AdmissionStatusEvents.Add(application.RecordCreated(AdmissionMapping.Today(clock), userId, userEmail, now));
        audit.Add(AuditAction.AdmissionApplicationCreated, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return application.Id;
    }
}

public record ListAdmissionsQuery(AdmissionStatus? Status, DestinationCountry? Country, string? Search, int Page = 1, int PageSize = 25) : IQuery<PagedResult<AdmissionSummary>>;

public class ListAdmissionsValidator : AbstractValidator<ListAdmissionsQuery>
{
    public ListAdmissionsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class ListAdmissionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListAdmissionsQuery, PagedResult<AdmissionSummary>>
{
    public async ValueTask<PagedResult<AdmissionSummary>> Handle(ListAdmissionsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var applications = db.AdmissionApplications.AsNoTracking().AsQueryable();
        if (query.Status is { } status) applications = applications.Where(a => a.Status == status);
        if (query.Country is { } country) applications = applications.Where(a => a.Country == country);

        var rows = AdmissionMapping.Rows(db, applications);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(s => s.CandidateName.ToLower().Contains(term) || s.InstitutionName.ToLower().Contains(term) || s.ProgrammeName.ToLower().Contains(term));
        }

        var total = await rows.CountAsync(cancellationToken);
        var items = await rows.OrderByDescending(s => s.StatusChangedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<AdmissionSummary>(items.Select(r => r.ToSummary()).ToList(), query.Page, query.PageSize, total);
    }
}

public record ListCandidateAdmissionsQuery(Guid CandidateId) : IQuery<IReadOnlyList<AdmissionSummary>>;

public class ListCandidateAdmissionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListCandidateAdmissionsQuery, IReadOnlyList<AdmissionSummary>>
{
    public async ValueTask<IReadOnlyList<AdmissionSummary>> Handle(ListCandidateAdmissionsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var rows = await AdmissionMapping.Rows(db, db.AdmissionApplications.AsNoTracking().Where(a => a.CandidateId == query.CandidateId))
            .OrderByDescending(s => s.StatusChangedAtUtc).ToListAsync(cancellationToken);
        return rows.Select(r => r.ToSummary()).ToList();
    }
}

public record GetAdmissionQuery(Guid Id) : IQuery<AdmissionDetail>;

public class GetAdmissionHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetAdmissionQuery, AdmissionDetail>
{
    public async ValueTask<AdmissionDetail> Handle(GetAdmissionQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await AdmissionMapping.DetailAsync(db, query.Id, cancellationToken);
    }
}

/// <summary>Records a move that staff made or learned of on the school's portal.</summary>
public record ChangeAdmissionStatusCommand(Guid Id, AdmissionStatus ToStatus, DateOnly? OccurredOn, string? Note, string? InstitutionReference, string? PortalUrl, bool AcknowledgeDocumentGaps = false) : ICommand<AdmissionDetail>;

public class ChangeAdmissionStatusValidator : AbstractValidator<ChangeAdmissionStatusCommand>
{
    public ChangeAdmissionStatusValidator()
    {
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.InstitutionReference).MaximumLength(100);
        RuleFor(x => x.PortalUrl).OptionalWebAddress();
    }
}

public class ChangeAdmissionStatusHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<ChangeAdmissionStatusCommand, AdmissionDetail>
{
    public async ValueTask<AdmissionDetail> Handle(ChangeAdmissionStatusCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.AdmissionsStaff);
        var application = await db.AdmissionApplications.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Application not found.");

        if (command.ToStatus == AdmissionStatus.SubmittedToInstitution
            && !await db.ApplicationForms.AnyAsync(f => f.CandidateId == application.CandidateId && f.Status == FormStatus.Submitted, cancellationToken))
        {
            throw new DomainException("The candidate's Nichotan application form must be signed and submitted before this can be sent to an institution.");
        }

        // Documents: staff may go ahead without them, but only knowingly, and the history says so.
        var note = command.Note;
        if (command.ToStatus is AdmissionStatus.ReadyForReview or AdmissionStatus.SubmittedToInstitution && AdmissionWorkflow.CanMove(application.Status, command.ToStatus))
        {
            var gaps = (await Slots.ChecklistAsync(db, application.Id, cancellationToken)).Gaps;
            var count = command.ToStatus == AdmissionStatus.ReadyForReview ? gaps.Missing : gaps.NotAccepted;
            if (count > 0)
            {
                if (!command.AcknowledgeDocumentGaps)
                {
                    var what = command.ToStatus == AdmissionStatus.ReadyForReview ? "still missing" : "not yet accepted";
                    throw new DomainException($"{count} required document{(count == 1 ? " is" : "s are")} {what}. Confirm you want to continue without {(count == 1 ? "it" : "them")}.");
                }

                note = string.IsNullOrWhiteSpace(note) ? "Proceeded with required documents outstanding." : $"{note.Trim()} (Proceeded with required documents outstanding.)";
            }
        }

        var today = AdmissionMapping.Today(clock);
        var from = application.Status;
        var recorded = application.MoveTo(command.ToStatus, command.OccurredOn ?? today, note, userId, current.Email, today, clock.UtcNow);
        if (command.InstitutionReference is not null || command.PortalUrl is not null)
        {
            application.UpdateDetails(command.InstitutionReference ?? application.InstitutionReference, command.PortalUrl ?? application.PortalUrl);
        }

        db.AdmissionStatusEvents.Add(recorded);
        audit.Add(AuditAction.AdmissionStatusChanged, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { from = from.ToString(), to = command.ToStatus.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return await AdmissionMapping.DetailAsync(db, application.Id, cancellationToken);
    }
}

/// <summary>Fixes a wrongly recorded status. Administrators only; the original entry stays in the history.</summary>
public record CorrectAdmissionStatusCommand(Guid Id, AdmissionStatus ToStatus, DateOnly? OccurredOn, string Note) : ICommand<AdmissionDetail>;

public class CorrectAdmissionStatusValidator : AbstractValidator<CorrectAdmissionStatusCommand>
{
    public CorrectAdmissionStatusValidator() => RuleFor(x => x.Note).NotEmpty().MaximumLength(2000);
}

public class CorrectAdmissionStatusHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<CorrectAdmissionStatusCommand, AdmissionDetail>
{
    public async ValueTask<AdmissionDetail> Handle(CorrectAdmissionStatusCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Administrator);
        var application = await db.AdmissionApplications.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Application not found.");
        var today = AdmissionMapping.Today(clock);
        var from = application.Status;
        var recorded = application.Correct(command.ToStatus, command.OccurredOn ?? today, command.Note, userId, current.Email, today, clock.UtcNow);
        db.AdmissionStatusEvents.Add(recorded);
        audit.Add(AuditAction.AdmissionStatusCorrected, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { from = from.ToString(), to = command.ToStatus.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return await AdmissionMapping.DetailAsync(db, application.Id, cancellationToken);
    }
}

public record UpdateAdmissionDetailsCommand(Guid Id, string? InstitutionReference, string? PortalUrl) : ICommand<AdmissionDetail>;

public class UpdateAdmissionDetailsValidator : AbstractValidator<UpdateAdmissionDetailsCommand>
{
    public UpdateAdmissionDetailsValidator()
    {
        RuleFor(x => x.InstitutionReference).MaximumLength(100);
        RuleFor(x => x.PortalUrl).OptionalWebAddress();
    }
}

public class UpdateAdmissionDetailsHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<UpdateAdmissionDetailsCommand, AdmissionDetail>
{
    public async ValueTask<AdmissionDetail> Handle(UpdateAdmissionDetailsCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var application = await db.AdmissionApplications.FirstOrDefaultAsync(a => a.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Application not found.");
        application.UpdateDetails(command.InstitutionReference, command.PortalUrl);
        audit.Add(AuditAction.AdmissionDetailsUpdated, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await AdmissionMapping.DetailAsync(db, application.Id, cancellationToken);
    }
}

// ─────────────────────────── The candidate's own view ───────────────────────────

public record GetMyAdmissionsQuery : IQuery<IReadOnlyList<MyAdmissionResult>>;

public class GetMyAdmissionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyAdmissionsQuery, IReadOnlyList<MyAdmissionResult>>
{
    public async ValueTask<IReadOnlyList<MyAdmissionResult>> Handle(GetMyAdmissionsQuery query, CancellationToken cancellationToken) =>
        await StudentApplications.MineAsync(db, await StudentApplications.ProfileIdAsync(db, current, cancellationToken), cancellationToken);
}

/// <summary>
/// Active, Offers and Total are the current pipeline (how many applications are in a state now), not movement in a month.
/// OffersIssued counts the applications that have ever been recorded as receiving an offer, so an offer still counts after
/// the application moves on.
/// </summary>
public record AdmissionStats(int Active, int Offers, int OffersIssued, int Total, int ToReview, int Acceptances, int PalFollowUps, int PalOverdue);

public record GetAdmissionStatsQuery : IQuery<AdmissionStats>;

public class GetAdmissionStatsHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<GetAdmissionStatsQuery, AdmissionStats>
{
    public async ValueTask<AdmissionStats> Handle(GetAdmissionStatsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var applications = db.AdmissionApplications.AsNoTracking();
        var inactive = new[] { AdmissionStatus.Rejected, AdmissionStatus.Withdrawn, AdmissionStatus.Closed };
        var active = await applications.CountAsync(a => !inactive.Contains(a.Status), cancellationToken);
        var offers = await applications.CountAsync(a => a.Status == AdmissionStatus.OfferReceived, cancellationToken);
        var issued = await db.AdmissionStatusEvents.AsNoTracking().Where(e => e.To == AdmissionStatus.OfferReceived).Select(e => e.ApplicationId).Distinct().CountAsync(cancellationToken);
        var toReview = await applications.CountAsync(a => a.Status == AdmissionStatus.ReadyForReview, cancellationToken);
        var acceptances = await db.AdmissionAcceptances.AsNoTracking().CountAsync(cancellationToken);
        var pal = await PalFollowUps.ListAsync(db, clock.UtcNow, cancellationToken);
        return new AdmissionStats(active, offers, issued, await applications.CountAsync(cancellationToken), toReview, acceptances, pal.Count, pal.Count(p => p.Overdue));
    }
}
