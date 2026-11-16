using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Application.Documents;
using Nichotan.Application.Identity;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Admissions;

// ─────────────────────────── What a student sees and does ───────────────────────────
//
// Students create their own applications from the directory and send them to their counsellor. Staff then review each
// one and submit it on the school's own portal, which we don't control: a student never marks anything as submitted there.

public record DirectoryProgramme(Guid Id, string Name, ApplicantLevel Level, IReadOnlyList<IntakeDto> Intakes);

public record DirectoryInstitution(Guid Id, string Name, DestinationCountry Country, string? Website, IReadOnlyList<DirectoryProgramme> Programmes);

/// <summary>The schools and programmes a student can apply to. No staff notes, and nothing archived.</summary>
public record GetStudentDirectoryQuery : IQuery<IReadOnlyList<DirectoryInstitution>>;

public class GetStudentDirectoryHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<GetStudentDirectoryQuery, IReadOnlyList<DirectoryInstitution>>
{
    public async ValueTask<IReadOnlyList<DirectoryInstitution>> Handle(GetStudentDirectoryQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Candidate);
        var year = clock.UtcNow.Year;
        var institutions = await db.Institutions.AsNoTracking().Where(i => !i.IsArchived).OrderBy(i => i.Country).ThenBy(i => i.Name).ToListAsync(cancellationToken);
        var programmes = await db.Programmes.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Name).ToListAsync(cancellationToken);
        return institutions
            .Select(i => new DirectoryInstitution(i.Id, i.Name, i.Country, i.Website, programmes
                .Where(p => p.InstitutionId == i.Id)
                .Select(p => new DirectoryProgramme(p.Id, p.Name, p.Level, p.Intakes.Where(x => x.Year >= year).Select(x => new IntakeDto(x.Term, x.Year)).ToList()))
                .Where(p => p.Intakes.Count > 0)
                .ToList()))
            .Where(i => i.Programmes.Count > 0)
            .ToList();
    }
}

internal static class StudentApplications
{
    public const int MaxOpen = 5;

    public static async Task<Guid> ProfileIdAsync(IAppDbContext db, ICurrentUser current, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        return (await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) ?? throw new NotFoundException("Candidate profile not found.")).Id;
    }

    public static string Blocker(string typeName) => typeName == "Nichotan application form" ? "Sign and submit your application form" : $"Upload {typeName}";

    /// <summary>The student's own applications, each with what stops it being sent and whether it can still be withdrawn.</summary>
    public static async Task<IReadOnlyList<MyAdmissionResult>> MineAsync(IAppDbContext db, Guid candidateId, CancellationToken cancellationToken)
    {
        var rows = await AdmissionMapping.Rows(db, db.AdmissionApplications.AsNoTracking().Where(a => a.CandidateId == candidateId)).OrderByDescending(s => s.StatusChangedAtUtc).ToListAsync(cancellationToken);
        var ids = rows.Select(r => r.Id).ToList();
        var steps = await db.AdmissionStatusEvents.AsNoTracking().Where(e => ids.Contains(e.ApplicationId)).OrderBy(e => e.RecordedAtUtc).ToListAsync(cancellationToken);

        var offers = await db.AdmissionOffers.AsNoTracking().Where(o => ids.Contains(o.ApplicationId)).ToListAsync(cancellationToken);
        var acceptances = await db.AdmissionAcceptances.AsNoTracking().Where(a => ids.Contains(a.ApplicationId)).ToListAsync(cancellationToken);
        var docs = await Outcomes.DocsAsync(db, ids, cancellationToken);
        // Which kind of document the offer letter is, so the student's upload can name it (they can't read the staff list).
        Guid? offerLetterTypeId = await db.DocumentTypes.AsNoTracking().Where(t => t.Purpose == DocumentPurpose.OfferLetter).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(cancellationToken);

        var results = new List<MyAdmissionResult>();
        foreach (var r in rows)
        {
            var sendable = r.Status is AdmissionStatus.Draft or AdmissionStatus.DocumentsRequired;
            IReadOnlyList<string> blockers = [];
            if (sendable) blockers = (await Slots.ChecklistAsync(db, r.Id, cancellationToken)).Gaps.MissingNames.Select(Blocker).ToList();
            results.Add(new MyAdmissionResult(
                r.Id, r.InstitutionName, r.ProgrammeName, r.Country, r.IntakeTerm, r.IntakeYear, r.Status, r.StatusChangedAtUtc, r.SubmittedToInstitutionOn,
                steps.Where(e => e.ApplicationId == r.Id).Select(e => new MyAdmissionStep(e.To, e.OccurredOn)).ToList(),
                CanSend: sendable && blockers.Count == 0,
                CanWithdraw: r.Status is AdmissionStatus.Draft or AdmissionStatus.DocumentsRequired or AdmissionStatus.ReadyForReview,
                Blockers: blockers,
                Offer: offers.FirstOrDefault(o => o.ApplicationId == r.Id) is { } offer
                    ? new MyOffer(offer.Conditions, offer.RespondByOn, docs.GetValueOrDefault((r.Id, DocumentPurpose.OfferLetter))?.DocumentId, docs.GetValueOrDefault((r.Id, DocumentPurpose.OfferLetter))?.FileName)
                    : null,
                Acceptance: acceptances.FirstOrDefault(a => a.ApplicationId == r.Id) is { } acceptance
                    ? new MyAcceptance(acceptance.ReceivedOn, acceptance.PalNumber, docs.GetValueOrDefault((r.Id, DocumentPurpose.AcceptanceLetter))?.DocumentId, docs.GetValueOrDefault((r.Id, DocumentPurpose.PalLetter))?.DocumentId)
                    : null,
                CanUploadOfferLetter: AdmissionWorkflow.AcceptsOfferLetter(r.Status),
                OfferLetter: docs.GetValueOrDefault((r.Id, DocumentPurpose.OfferLetter)) is { } letter ? new MyFile(letter.DocumentId, letter.FileName) : null,
                OfferLetterTypeId: offerLetterTypeId));
        }

        return results;
    }

    public static async Task<MyAdmissionResult> OneAsync(IAppDbContext db, Guid candidateId, Guid id, CancellationToken cancellationToken) =>
        (await MineAsync(db, candidateId, cancellationToken)).FirstOrDefault(a => a.Id == id) ?? throw new NotFoundException("Application not found.");

    /// <summary>One of the student's own applications, tracked for change. Someone else's looks exactly like one that doesn't exist.</summary>
    public static async Task<AdmissionApplication> OwnAsync(IAppDbContext db, Guid candidateId, Guid id, CancellationToken cancellationToken) =>
        await db.AdmissionApplications.FirstOrDefaultAsync(a => a.Id == id && a.CandidateId == candidateId, cancellationToken) ?? throw new NotFoundException("Application not found.");
}

public record CreateMyAdmissionCommand(Guid ProgrammeId, StartTerm IntakeTerm, int IntakeYear) : ICommand<MyAdmissionResult>;

public class CreateMyAdmissionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<CreateMyAdmissionCommand, MyAdmissionResult>
{
    public async ValueTask<MyAdmissionResult> Handle(CreateMyAdmissionCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);

        var inactive = new[] { AdmissionStatus.Rejected, AdmissionStatus.Withdrawn, AdmissionStatus.Closed };
        var open = await db.AdmissionApplications.CountAsync(a => a.CandidateId == candidateId && !inactive.Contains(a.Status), cancellationToken);
        if (open >= StudentApplications.MaxOpen) throw new DomainException($"You can have up to {StudentApplications.MaxOpen} open applications. Withdraw one before adding another, or ask your counsellor.");

        // A programme at a different level from the one on their own form is almost certainly a mistake.
        var programme = await db.Programmes.AsNoTracking().FirstOrDefaultAsync(p => p.Id == command.ProgrammeId && !p.IsArchived, cancellationToken) ?? throw new NotFoundException("Programme not found.");
        var formJson = await db.ApplicationForms.AsNoTracking().Where(f => f.CandidateId == candidateId).Select(f => f.DataJson).FirstOrDefaultAsync(cancellationToken);
        if (formJson is not null && FormJson.Deserialize(formJson).Program.ProgramLevel is { } level && ProgramLevels.ApplicantLevelOf(level) != programme.Level)
        {
            throw new DomainException("That programme is for a different level of study from the one on your application form. Choose another programme, or update your form.");
        }

        var id = await AdmissionCreator.CreateAsync(db, audit, clock, userId, current.Email, candidateId, command.ProgrammeId, command.IntakeTerm, command.IntakeYear, cancellationToken);
        return await StudentApplications.OneAsync(db, candidateId, id, cancellationToken);
    }
}

public record WithdrawMyAdmissionCommand(Guid Id) : ICommand<MyAdmissionResult>;

public class WithdrawMyAdmissionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<WithdrawMyAdmissionCommand, MyAdmissionResult>
{
    public async ValueTask<MyAdmissionResult> Handle(WithdrawMyAdmissionCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var application = await StudentApplications.OwnAsync(db, candidateId, command.Id, cancellationToken);

        // Once staff have submitted it to the school, only staff can withdraw it there, so the student asks their counsellor.
        if (application.Status is not (AdmissionStatus.Draft or AdmissionStatus.DocumentsRequired or AdmissionStatus.ReadyForReview))
        {
            throw new DomainException("This application is already with the school. Ask your counsellor if you want to withdraw it.");
        }

        var today = AdmissionMapping.Today(clock);
        var from = application.Status;
        db.AdmissionStatusEvents.Add(application.MoveTo(AdmissionStatus.Withdrawn, today, "Withdrawn by the student.", userId, current.Email, today, clock.UtcNow));
        audit.Add(AuditAction.AdmissionStatusChanged, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { from = from.ToString(), to = "Withdrawn", by = "student" });
        await db.SaveChangesAsync(cancellationToken);
        return await StudentApplications.OneAsync(db, candidateId, application.Id, cancellationToken);
    }
}

/// <summary>The student finishes their part and hands the application to their counsellor for review.</summary>
public record SendMyAdmissionCommand(Guid Id) : ICommand<MyAdmissionResult>;

public class SendMyAdmissionHandler(
    IAppDbContext db,
    ICurrentUser current,
    IAuditLog audit,
    IClock clock,
    IEmailSender email,
    IAppUrls urls,
    IOptions<AdmissionNotificationOptions> notify,
    ILogger<SendMyAdmissionHandler> logger) : ICommandHandler<SendMyAdmissionCommand, MyAdmissionResult>
{
    public async ValueTask<MyAdmissionResult> Handle(SendMyAdmissionCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var application = await StudentApplications.OwnAsync(db, candidateId, command.Id, cancellationToken);
        if (application.Status is not (AdmissionStatus.Draft or AdmissionStatus.DocumentsRequired)) throw new DomainException("This application has already been sent to your counsellor.");

        // The signed form and every required document must be uploaded. Accepting them is the counsellor's job, after this.
        var gaps = (await Slots.ChecklistAsync(db, application.Id, cancellationToken)).Gaps;
        if (gaps.Missing > 0) throw new DomainException("Before you send this: " + string.Join("; ", gaps.MissingNames.Select(StudentApplications.Blocker).Select(b => b[..1].ToLowerInvariant() + b[1..])) + ".");

        var today = AdmissionMapping.Today(clock);
        var from = application.Status;
        db.AdmissionStatusEvents.Add(application.MoveTo(AdmissionStatus.ReadyForReview, today, "Sent to the counsellor by the student.", userId, current.Email, today, clock.UtcNow));
        audit.Add(AuditAction.AdmissionStatusChanged, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { from = from.ToString(), to = "ReadyForReview", by = "student" });
        await db.SaveChangesAsync(cancellationToken);

        // The team is told once it's saved; a mail failure never undoes the student's send.
        if (!string.IsNullOrWhiteSpace(notify.Value.NotifyEmail))
        {
            var student = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == candidateId).Select(p => p.FirstName + " " + p.LastName).FirstAsync(cancellationToken);
            var school = await db.Institutions.AsNoTracking().Where(i => i.Id == application.InstitutionId).Select(i => i.Name).FirstAsync(cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.ApplicationSent(notify.Value.NotifyEmail.Trim(), student, school, $"{urls.WebBaseUrl}/staff/admissions/{application.Id}"), logger, cancellationToken);
        }

        return await StudentApplications.OneAsync(db, candidateId, application.Id, cancellationToken);
    }
}

// ─────────────────────────── A school that isn't in the directory ───────────────────────────

public record SuggestionResult(Guid Id, Guid CandidateId, string CandidateName, string InstitutionName, DestinationCountry Country, string ProgrammeName, StartTerm? IntakeTerm, int? IntakeYear, string? Note, SuggestionStatus Status, string? StaffNote, DateTime CreatedAtUtc);

internal static class SuggestionMapping
{
    public static async Task<List<SuggestionResult>> ToResultsAsync(IAppDbContext db, IEnumerable<SchoolSuggestion> suggestions, CancellationToken cancellationToken)
    {
        var list = suggestions.ToList();
        var ids = list.Select(s => s.CandidateId).Distinct().ToList();
        var names = await db.CandidateProfiles.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken);
        return list.Select(s => new SuggestionResult(s.Id, s.CandidateId, names.GetValueOrDefault(s.CandidateId, ""), s.InstitutionName, s.Country, s.ProgrammeName, s.IntakeTerm, s.IntakeYear, s.Note, s.Status, s.StaffNote, s.CreatedAtUtc)).ToList();
    }
}

public record SuggestSchoolCommand(string InstitutionName, DestinationCountry Country, string ProgrammeName, StartTerm? IntakeTerm, int? IntakeYear, string? Note) : ICommand<SuggestionResult>;

public class SuggestSchoolValidator : AbstractValidator<SuggestSchoolCommand>
{
    public SuggestSchoolValidator()
    {
        RuleFor(x => x.InstitutionName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.ProgrammeName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Country).IsInEnum();
        RuleFor(x => x.IntakeYear).InclusiveBetween(2000, 2100).When(x => x.IntakeYear is not null);
        RuleFor(x => x.Note).MaximumLength(1000);
    }
}

public class SuggestSchoolHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SuggestSchoolCommand, SuggestionResult>
{
    public async ValueTask<SuggestionResult> Handle(SuggestSchoolCommand command, CancellationToken cancellationToken)
    {
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var pending = await db.SchoolSuggestions.CountAsync(s => s.CandidateId == candidateId && s.Status == SuggestionStatus.New, cancellationToken);
        if (pending >= 10) throw new DomainException("You already have several suggestions waiting. Your counsellor will get to them soon.");

        var suggestion = new SchoolSuggestion(candidateId, command.InstitutionName, command.Country, command.ProgrammeName, command.IntakeTerm, command.IntakeYear, command.Note);
        db.SchoolSuggestions.Add(suggestion);
        audit.Add(AuditAction.SchoolSuggested, entityType: nameof(SchoolSuggestion), entityId: suggestion.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return (await SuggestionMapping.ToResultsAsync(db, [suggestion], cancellationToken))[0];
    }
}

public record ListMySuggestionsQuery : IQuery<IReadOnlyList<SuggestionResult>>;

public class ListMySuggestionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListMySuggestionsQuery, IReadOnlyList<SuggestionResult>>
{
    public async ValueTask<IReadOnlyList<SuggestionResult>> Handle(ListMySuggestionsQuery query, CancellationToken cancellationToken)
    {
        var candidateId = await StudentApplications.ProfileIdAsync(db, current, cancellationToken);
        var mine = await db.SchoolSuggestions.AsNoTracking().Where(s => s.CandidateId == candidateId).OrderByDescending(s => s.CreatedAtUtc).ToListAsync(cancellationToken);
        // The student sees whether it was dealt with, not staff's private notes.
        return (await SuggestionMapping.ToResultsAsync(db, mine, cancellationToken)).Select(s => s with { StaffNote = null }).ToList();
    }
}

public record ListSuggestionsQuery(SuggestionStatus? Status) : IQuery<IReadOnlyList<SuggestionResult>>;

public class ListSuggestionsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListSuggestionsQuery, IReadOnlyList<SuggestionResult>>
{
    public async ValueTask<IReadOnlyList<SuggestionResult>> Handle(ListSuggestionsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var suggestions = db.SchoolSuggestions.AsNoTracking().AsQueryable();
        if (query.Status is { } status) suggestions = suggestions.Where(s => s.Status == status);
        return await SuggestionMapping.ToResultsAsync(db, await suggestions.OrderBy(s => s.Status).ThenByDescending(s => s.CreatedAtUtc).Take(200).ToListAsync(cancellationToken), cancellationToken);
    }
}

public record UpdateSuggestionCommand(Guid Id, SuggestionStatus Status, string? StaffNote) : ICommand<SuggestionResult>;

public class UpdateSuggestionValidator : AbstractValidator<UpdateSuggestionCommand>
{
    public UpdateSuggestionValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.StaffNote).MaximumLength(1000);
    }
}

public class UpdateSuggestionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<UpdateSuggestionCommand, SuggestionResult>
{
    public async ValueTask<SuggestionResult> Handle(UpdateSuggestionCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var suggestion = await db.SchoolSuggestions.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Suggestion not found.");
        suggestion.Update(command.Status, command.StaffNote, current.Email, clock.UtcNow);
        audit.Add(AuditAction.SchoolSuggestionUpdated, entityType: nameof(SchoolSuggestion), entityId: suggestion.Id.ToString(), details: new { to = command.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return (await SuggestionMapping.ToResultsAsync(db, [suggestion], cancellationToken))[0];
    }
}
