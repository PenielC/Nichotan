using System.Security.Cryptography;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Admissions;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Visas;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Documents;

// ─────────────────────────── Results ───────────────────────────

public enum SlotStatus { Missing, Uploaded, UnderReview, Accepted, NeedsResubmission }

public record DocumentTypeResult(Guid Id, string Name, string? Description, DocumentScope Scope, bool TracksExpiry, bool IsAutomatic, bool IsActive, int SortOrder, DocumentPurpose Purpose, bool AllowsMultipleFiles = false);

public record RequirementResult(Guid Id, Guid DocumentTypeId, string DocumentTypeName, DestinationCountry? Country, ApplicantLevel? Level, Guid? InstitutionId, string? InstitutionName, bool IsRequired, bool IsActive, ChecklistStage Stage);

/// <summary>One document a candidate is asked for, and where it stands.</summary>
public record DocumentSlot(
    Guid DocumentTypeId,
    string TypeName,
    string? Description,
    DocumentScope Scope,
    bool TracksExpiry,
    bool IsAutomatic,
    Guid? ApplicationId,
    string? ApplicationLabel,
    bool Required,
    SlotStatus Status,
    Guid? DocumentId,
    int Version,
    string? FileName,
    DateTime? UploadedAtUtc,
    DateOnly? ExpiresOn,
    string? ReviewNote,
    DocumentPurpose Purpose,
    bool NeedsReview = true,
    bool AllowsMultipleFiles = false,
    IReadOnlyList<SlotFile>? Files = null);

/// <summary>One file in a document, as it stands now. <see cref="VersionNumber"/> is what downloads and replacements refer to.</summary>
public record SlotFile(int Position, int VersionNumber, string FileName, long SizeBytes, DateTime UploadedAtUtc, DateOnly? ExpiresOn);

public record DocumentsOverview(Guid CandidateId, IReadOnlyList<DocumentSlot> Slots);

public record ChecklistLine(DocumentSlot Slot, ChecklistSource Source, string? Note);

public record DocumentGaps(int Missing, int NotAccepted, IReadOnlyList<string> MissingNames);

public record ChecklistResult(Guid ApplicationId, IReadOnlyList<ChecklistLine> Items, DocumentGaps Gaps);

public record DocumentVersionResult(int VersionNumber, string FileName, string ContentType, long SizeBytes, DateTime UploadedAtUtc, string? UploadedBy, DateOnly? ExpiresOn, int Position = 1, bool Removed = false);

public record DocumentReviewResult(int VersionNumber, DocumentStatus Decision, string? Note, string? Reviewer, DateTime ReviewedAtUtc);

public record DocumentDetail(Guid Id, Guid CandidateId, Guid DocumentTypeId, string TypeName, Guid? ApplicationId, DocumentStatus Status, int CurrentVersion, IReadOnlyList<DocumentVersionResult> Versions, IReadOnlyList<DocumentReviewResult> Reviews);

public record ReviewQueueItem(Guid DocumentId, Guid CandidateId, string CandidateName, string TypeName, DocumentStatus Status, int Version, string FileName, DateTime UploadedAtUtc, int FileCount = 1);

public record FileDownload(Stream Content, string FileName, string ContentType);

// ─────────────────────────── Building slots ───────────────────────────

/// <summary>What one slot should show, before it is matched to uploads.</summary>
internal record Wanted(Guid TypeId, Guid? ApplicationId, string? ApplicationLabel, bool Required);

/// <summary>Which files a document holds now: the latest upload at each position, leaving out any that were taken out.</summary>
internal static class DocumentFiles
{
    public static List<DocumentVersion> Live(IEnumerable<DocumentVersion> versions) =>
        versions.GroupBy(v => v.Position).Select(g => g.OrderByDescending(v => v.VersionNumber).First()).Where(v => v.RemovedAtUtc is null).OrderBy(v => v.Position).ToList();

    public static SlotFile ToSlotFile(DocumentVersion v) => new(v.Position, v.VersionNumber, v.FileName, v.SizeBytes, v.UploadedAtUtc, v.ExpiresOn);
}

internal static class Slots
{
    public static string Label(Institution i, Programme p, AdmissionApplication a) => $"{i.Name}: {p.Name} ({a.IntakeTerm} {a.IntakeYear})";

    /// <summary>Matches each wanted slot to the candidate's uploads and returns them in the order the types are listed.</summary>
    public static async Task<List<DocumentSlot>> BuildAsync(IAppDbContext db, Guid candidateId, IReadOnlyList<Wanted> wanted, CancellationToken cancellationToken)
    {
        var types = await db.DocumentTypes.AsNoTracking().ToListAsync(cancellationToken);
        var documents = await db.CandidateDocuments.AsNoTracking().Where(d => d.CandidateId == candidateId).ToListAsync(cancellationToken);
        var ids = documents.Select(d => d.Id).ToList();
        var versions = await db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync(cancellationToken);
        var reviews = await db.DocumentReviews.AsNoTracking().Where(r => ids.Contains(r.DocumentId)).ToListAsync(cancellationToken);
        var formSubmitted = await db.ApplicationForms.AsNoTracking().AnyAsync(f => f.CandidateId == candidateId && f.Status == FormStatus.Submitted, cancellationToken);
        var visaOnly = await VisaOnlyTypeIdsAsync(db, cancellationToken);

        var slots = new List<(int Order, DocumentSlot Slot)>();
        foreach (var w in wanted)
        {
            var type = types.FirstOrDefault(t => t.Id == w.TypeId);
            if (type is null) continue;
            var doc = documents.FirstOrDefault(d => d.DocumentTypeId == type.Id && d.ApplicationId == (type.Scope == DocumentScope.Application ? w.ApplicationId : null));
            DocumentSlot slot;
            if (type.IsAutomatic)
            {
                slot = new DocumentSlot(type.Id, type.Name, type.Description, type.Scope, false, true, null, null, w.Required, formSubmitted ? SlotStatus.Accepted : SlotStatus.Missing, null, 0, null, null, null, null, type.Purpose);
            }
            else if (doc is null || !DocumentFiles.Live(versions.Where(v => v.DocumentId == doc.Id)).Any())
            {
                // Nothing uploaded, or every file was taken out again.
                slot = new DocumentSlot(type.Id, type.Name, type.Description, type.Scope, type.TracksExpiry, false, type.Scope == DocumentScope.Application ? w.ApplicationId : null, type.Scope == DocumentScope.Application ? w.ApplicationLabel : null, w.Required, SlotStatus.Missing, null, 0, null, null, null, null, type.Purpose, AllowsMultipleFiles: type.AllowsMultipleFiles, Files: []);
            }
            else
            {
                var files = DocumentFiles.Live(versions.Where(v => v.DocumentId == doc.Id));
                var version = files.OrderByDescending(f => f.VersionNumber).First();
                var lastReview = reviews.Where(r => r.DocumentId == doc.Id).OrderByDescending(r => r.ReviewedAtUtc).FirstOrDefault();
                var note = lastReview is { Decision: DocumentStatus.NeedsResubmission } && doc.Status == DocumentStatus.NeedsResubmission ? lastReview.Note : null;
                slot = new DocumentSlot(type.Id, type.Name, type.Description, type.Scope, type.TracksExpiry, false, doc.ApplicationId, doc.ApplicationId is null ? null : w.ApplicationLabel, w.Required,
                    (SlotStatus)(int)(doc.Status + 1), doc.Id, doc.CurrentVersion, version.FileName, version.UploadedAtUtc, version.ExpiresOn, note, type.Purpose, AllowsMultipleFiles: type.AllowsMultipleFiles, Files: files.Select(DocumentFiles.ToSlotFile).ToList());
            }

            // Visa documents are only collected here so the visa application can be lodged; the authority reviews them on its own platform.
            if (visaOnly.Contains(type.Id)) slot = slot with { NeedsReview = false };
            slots.Add((type.SortOrder, slot));
        }

        return slots.OrderBy(s => s.Order).ThenBy(s => s.Slot.ApplicationLabel).Select(s => s.Slot).ToList();
    }

    /// <summary>
    /// Document types that only the visa checklist asks for. They are uploaded here so staff can lodge the visa application on the
    /// authority's platform, which does the reviewing, so they never go through document review in this system.
    /// </summary>
    public static async Task<HashSet<Guid>> VisaOnlyTypeIdsAsync(IAppDbContext db, CancellationToken cancellationToken)
    {
        var admission = await db.DocumentRequirements.AsNoTracking().Where(r => r.Stage == ChecklistStage.Admission).Select(r => r.DocumentTypeId)
            .Union(db.ChecklistItems.AsNoTracking().Where(i => i.Stage == ChecklistStage.Admission).Select(i => i.DocumentTypeId)).ToListAsync(cancellationToken);
        var visa = await db.DocumentRequirements.AsNoTracking().Where(r => r.Stage == ChecklistStage.Visa).Select(r => r.DocumentTypeId)
            .Union(db.ChecklistItems.AsNoTracking().Where(i => i.Stage == ChecklistStage.Visa).Select(i => i.DocumentTypeId)).ToListAsync(cancellationToken);
        return visa.Except(admission).ToHashSet();
    }

    public static DocumentGaps GapsOf(IEnumerable<DocumentSlot> slots)
    {
        var required = slots.Where(s => s.Required).ToList();
        var missing = required.Where(s => s.Status is SlotStatus.Missing or SlotStatus.NeedsResubmission).ToList();
        return new DocumentGaps(missing.Count, required.Count(s => s.Status != SlotStatus.Accepted), missing.Select(s => s.TypeName).Distinct().ToList());
    }

    /// <summary>The slots for one application's admission checklist.</summary>
    public static Task<ChecklistResult> ChecklistAsync(IAppDbContext db, Guid applicationId, CancellationToken cancellationToken) =>
        ChecklistAsync(db, applicationId, ChecklistStage.Admission, cancellationToken);

    /// <summary>The slots for one application's checklist at one stage (admission or visa).</summary>
    public static async Task<ChecklistResult> ChecklistAsync(IAppDbContext db, Guid applicationId, ChecklistStage stage, CancellationToken cancellationToken)
    {
        var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        var items = await db.ChecklistItems.AsNoTracking().Where(i => i.ApplicationId == applicationId && i.Stage == stage).ToListAsync(cancellationToken);
        var label = await LabelAsync(db, application, cancellationToken);
        var wanted = items.Select(i => new Wanted(i.DocumentTypeId, applicationId, label, i.IsRequired)).ToList();
        var slots = await BuildAsync(db, application.CandidateId, wanted, cancellationToken);
        var lines = slots.Select(s => new ChecklistLine(s, items.First(i => i.DocumentTypeId == s.DocumentTypeId).Source, items.First(i => i.DocumentTypeId == s.DocumentTypeId).Note)).ToList();
        // A visa document only has to be there: nothing here reviews it, so "not yet accepted" is the same as "missing".
        var gaps = GapsOf(slots);
        if (stage == ChecklistStage.Visa) gaps = gaps with { NotAccepted = gaps.Missing };
        return new ChecklistResult(applicationId, lines, gaps);
    }

    public static async Task<string> LabelAsync(IAppDbContext db, AdmissionApplication a, CancellationToken cancellationToken)
    {
        var institution = await db.Institutions.AsNoTracking().FirstAsync(i => i.Id == a.InstitutionId, cancellationToken);
        var programme = await db.Programmes.AsNoTracking().FirstAsync(p => p.Id == a.ProgrammeId, cancellationToken);
        return Label(institution, programme, a);
    }

    /// <summary>Everything a candidate is asked for across their open applications, plus what is always required.</summary>
    public static async Task<DocumentsOverview> OverviewAsync(IAppDbContext db, Guid candidateId, CancellationToken cancellationToken)
    {
        var wanted = new List<Wanted>();
        var types = await db.DocumentTypes.AsNoTracking().Where(t => t.IsActive).ToListAsync(cancellationToken);

        // Asked of everyone, before any application exists.
        var baseline = await db.DocumentRequirements.AsNoTracking().Where(r => r.IsActive && r.Stage == ChecklistStage.Admission && r.Country == null && r.Level == null && r.InstitutionId == null && r.IsRequired).ToListAsync(cancellationToken);
        foreach (var r in baseline.Where(r => types.Any(t => t.Id == r.DocumentTypeId && t.Scope == DocumentScope.Candidate))) wanted.Add(new Wanted(r.DocumentTypeId, null, null, true));

        // Asked because of the level (and country) chosen on their own application form, even before staff add an application.
        // So a postgraduate sees the CV and references straight away, and a high school applicant sees theirs.
        var formJson = await db.ApplicationForms.AsNoTracking().Where(f => f.CandidateId == candidateId).Select(f => f.DataJson).FirstOrDefaultAsync(cancellationToken);
        if (formJson is not null)
        {
            var program = FormJson.Deserialize(formJson).Program;
            if (program.ProgramLevel is { } programLevel)
            {
                var lines = await db.DocumentRequirements.AsNoTracking().Where(r => r.IsActive && r.Stage == ChecklistStage.Admission).ToListAsync(cancellationToken);
                foreach (var (typeId, required) in ChecklistLevels.ResolveBeforeInstitution(lines, program.DestinationCountry, ProgramLevels.ApplicantLevelOf(programLevel)))
                {
                    var type = types.FirstOrDefault(t => t.Id == typeId);
                    if (required && type is { Scope: DocumentScope.Candidate } && !wanted.Any(w => w.TypeId == typeId && w.ApplicationId is null)) wanted.Add(new Wanted(typeId, null, null, true));
                }
            }
        }

        var applications = await db.AdmissionApplications.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);
        var openVisas = (await db.VisaCases.AsNoTracking().Where(v => v.CandidateId == candidateId).ToListAsync(cancellationToken)).Where(v => !VisaWorkflow.IsFinished(v.Status)).Select(v => v.ApplicationId).ToHashSet();
        foreach (var a in applications.Where(a => AdmissionWorkflow.IsActive(a.Status) || openVisas.Contains(a.Id)))
        {
            var label = await LabelAsync(db, a, cancellationToken);
            var items = await db.ChecklistItems.AsNoTracking().Where(i => i.ApplicationId == a.Id).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                var visaItem = item.Stage == ChecklistStage.Visa;
                if (visaItem ? !openVisas.Contains(a.Id) : !AdmissionWorkflow.IsActive(a.Status)) continue;
                var type = types.FirstOrDefault(t => t.Id == item.DocumentTypeId);
                if (type is null) continue;
                if (type.Scope == DocumentScope.Candidate)
                {
                    var existing = wanted.FindIndex(w => w.TypeId == type.Id && w.ApplicationId is null);
                    if (existing < 0) wanted.Add(new Wanted(type.Id, null, null, item.IsRequired));
                    else if (item.IsRequired && !wanted[existing].Required) wanted[existing] = wanted[existing] with { Required = true };
                }
                else
                {
                    // The same document can be asked for at both stages; it is one slot, labelled for the visa when that is why it's there.
                    var existing = wanted.FindIndex(w => w.TypeId == type.Id && w.ApplicationId == a.Id);
                    if (existing < 0) wanted.Add(new Wanted(type.Id, a.Id, visaItem ? $"Visa: {label}" : label, item.IsRequired));
                    else if (item.IsRequired && !wanted[existing].Required) wanted[existing] = wanted[existing] with { Required = true };
                }
            }
        }

        // Anything already uploaded stays visible, even if no checklist asks for it now.
        var uploaded = await db.CandidateDocuments.AsNoTracking().Where(d => d.CandidateId == candidateId).ToListAsync(cancellationToken);
        foreach (var d in uploaded)
        {
            var type = types.FirstOrDefault(t => t.Id == d.DocumentTypeId);
            if (type is null || wanted.Any(w => w.TypeId == d.DocumentTypeId && w.ApplicationId == d.ApplicationId)) continue;
            var label = d.ApplicationId is { } appId && applications.FirstOrDefault(a => a.Id == appId) is { } app ? await LabelAsync(db, app, cancellationToken) : null;
            wanted.Add(new Wanted(d.DocumentTypeId, d.ApplicationId, label, false));
        }

        return new DocumentsOverview(candidateId, await BuildAsync(db, candidateId, wanted, cancellationToken));
    }

    /// <summary>Copies the template onto a new application's checklist.</summary>
    public static async Task SnapshotAsync(IAppDbContext db, AdmissionApplication application, CancellationToken cancellationToken, ChecklistStage stage = ChecklistStage.Admission)
    {
        foreach (var (typeId, required) in await ResolveTemplateAsync(db, application, stage, cancellationToken))
        {
            if (required) db.ChecklistItems.Add(new ApplicationChecklistItem(application.Id, typeId, true, ChecklistSource.Template, null, stage));
        }
    }

    public static Task<IReadOnlyList<(Guid DocumentTypeId, bool IsRequired)>> ResolveTemplateAsync(IAppDbContext db, AdmissionApplication application, CancellationToken cancellationToken) =>
        ResolveTemplateAsync(db, application, ChecklistStage.Admission, cancellationToken);

    public static async Task<IReadOnlyList<(Guid DocumentTypeId, bool IsRequired)>> ResolveTemplateAsync(IAppDbContext db, AdmissionApplication application, ChecklistStage stage, CancellationToken cancellationToken)
    {
        var activeTypes = await db.DocumentTypes.AsNoTracking().Where(t => t.IsActive).Select(t => t.Id).ToListAsync(cancellationToken);
        var lines = await db.DocumentRequirements.AsNoTracking().Where(r => r.IsActive && r.Stage == stage && activeTypes.Contains(r.DocumentTypeId)).ToListAsync(cancellationToken);
        return ChecklistRules.Resolve(lines, application.Country, application.Level, application.InstitutionId);
    }
}

// ─────────────────────────── Document types and the checklist template (administrators) ───────────────────────────

public record ListDocumentTypesQuery : IQuery<IReadOnlyList<DocumentTypeResult>>;

public class ListDocumentTypesHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListDocumentTypesQuery, IReadOnlyList<DocumentTypeResult>>
{
    public async ValueTask<IReadOnlyList<DocumentTypeResult>> Handle(ListDocumentTypesQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await db.DocumentTypes.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new DocumentTypeResult(t.Id, t.Name, t.Description, t.Scope, t.TracksExpiry, t.IsAutomatic, t.IsActive, t.SortOrder, t.Purpose, t.AllowsMultipleFiles)).ToListAsync(cancellationToken);
    }
}

public record SaveDocumentTypeCommand(Guid? Id, string Name, string? Description, DocumentScope Scope, bool TracksExpiry, bool IsActive, bool AllowsMultipleFiles = false) : ICommand<DocumentTypeResult>;

public class SaveDocumentTypeValidator : AbstractValidator<SaveDocumentTypeCommand>
{
    public SaveDocumentTypeValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
    }
}

public class SaveDocumentTypeHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SaveDocumentTypeCommand, DocumentTypeResult>
{
    public async ValueTask<DocumentTypeResult> Handle(SaveDocumentTypeCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var name = command.Name.Trim();
        DocumentType type;
        if (command.Id is { } id)
        {
            type = await db.DocumentTypes.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Document type not found.");
            if (type.Scope != command.Scope) throw new DomainException("A document type's scope can't change once it exists.");
            if (await db.DocumentTypes.AnyAsync(t => t.Id != id && t.Name.ToLower() == name.ToLower(), cancellationToken)) throw new ConflictException("A document type with that name already exists.");
            type.Update(command.Name, command.Description, command.TracksExpiry, command.AllowsMultipleFiles);
            type.SetActive(command.IsActive);
        }
        else
        {
            if (await db.DocumentTypes.AnyAsync(t => t.Name.ToLower() == name.ToLower(), cancellationToken)) throw new ConflictException("A document type with that name already exists.");
            var order = (await db.DocumentTypes.MaxAsync(t => (int?)t.SortOrder, cancellationToken) ?? 0) + 1;
            type = new DocumentType(command.Name, command.Description, command.Scope, command.TracksExpiry, isAutomatic: false, order, allowsMultipleFiles: command.AllowsMultipleFiles);
            db.DocumentTypes.Add(type);
        }

        audit.Add(AuditAction.DocumentRulesChanged, entityType: nameof(DocumentType), entityId: type.Id.ToString(), details: new { change = command.Id is null ? "created" : "updated" });
        await db.SaveChangesAsync(cancellationToken);
        return new DocumentTypeResult(type.Id, type.Name, type.Description, type.Scope, type.TracksExpiry, type.IsAutomatic, type.IsActive, type.SortOrder, type.Purpose, type.AllowsMultipleFiles);
    }
}

public record ListRequirementsQuery : IQuery<IReadOnlyList<RequirementResult>>;

public class ListRequirementsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListRequirementsQuery, IReadOnlyList<RequirementResult>>
{
    public async ValueTask<IReadOnlyList<RequirementResult>> Handle(ListRequirementsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var rows = await (from r in db.DocumentRequirements.AsNoTracking()
                          join t in db.DocumentTypes.AsNoTracking() on r.DocumentTypeId equals t.Id
                          join i in db.Institutions.AsNoTracking() on r.InstitutionId equals i.Id into institutions
                          from i in institutions.DefaultIfEmpty()
                          orderby t.SortOrder, r.Level, r.Country
                          select new { r, TypeName = t.Name, InstitutionName = i != null ? i.Name : null }).ToListAsync(cancellationToken);
        return rows.Select(x => new RequirementResult(x.r.Id, x.r.DocumentTypeId, x.TypeName, x.r.Country, x.r.Level, x.r.InstitutionId, x.InstitutionName, x.r.IsRequired, x.r.IsActive, x.r.Stage)).ToList();
    }
}

public record SaveRequirementCommand(Guid? Id, Guid DocumentTypeId, DestinationCountry? Country, ApplicantLevel? Level, Guid? InstitutionId, bool IsRequired, bool IsActive, ChecklistStage Stage = ChecklistStage.Admission) : ICommand<RequirementResult>;

public class SaveRequirementHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SaveRequirementCommand, RequirementResult>
{
    public async ValueTask<RequirementResult> Handle(SaveRequirementCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.DocumentTypeId, cancellationToken) ?? throw new NotFoundException("Document type not found.");
        if (type.Purpose != DocumentPurpose.None) throw new DomainException("The system manages this document, so it isn't part of the checklist.");
        string? institutionName = null;
        if (command.InstitutionId is { } institutionId)
        {
            institutionName = (await db.Institutions.AsNoTracking().FirstOrDefaultAsync(i => i.Id == institutionId, cancellationToken) ?? throw new NotFoundException("Institution not found.")).Name;
        }

        if (await db.DocumentRequirements.AnyAsync(r => r.Id != command.Id && r.Stage == command.Stage && r.DocumentTypeId == command.DocumentTypeId && r.Country == command.Country && r.Level == command.Level && r.InstitutionId == command.InstitutionId, cancellationToken))
        {
            throw new ConflictException("There is already a line for this document with the same stage, country, level and institution. Edit that one instead.");
        }

        DocumentRequirement line;
        if (command.Id is { } id)
        {
            line = await db.DocumentRequirements.FirstOrDefaultAsync(r => r.Id == id, cancellationToken) ?? throw new NotFoundException("Checklist line not found.");
            if (line.Stage != command.Stage) throw new DomainException("A checklist line can't move between the admission and visa checklists. Add a new line instead.");
            line.Update(command.Country, command.Level, command.InstitutionId, command.IsRequired, command.IsActive);
        }
        else
        {
            line = new DocumentRequirement(command.DocumentTypeId, command.Country, command.Level, command.InstitutionId, command.IsRequired, command.Stage);
            if (!command.IsActive) line.Update(command.Country, command.Level, command.InstitutionId, command.IsRequired, false);
            db.DocumentRequirements.Add(line);
        }

        // Existing applications keep the checklist they were created with; staff refresh them when they choose to.
        audit.Add(AuditAction.DocumentRulesChanged, entityType: nameof(DocumentRequirement), entityId: line.Id.ToString(), details: new { change = command.Id is null ? "created" : "updated" });
        await db.SaveChangesAsync(cancellationToken);
        return new RequirementResult(line.Id, type.Id, type.Name, line.Country, line.Level, line.InstitutionId, institutionName, line.IsRequired, line.IsActive, line.Stage);
    }
}

// ─────────────────────────── An application's checklist ───────────────────────────

public record GetAdmissionChecklistQuery(Guid ApplicationId) : IQuery<ChecklistResult>;

public class GetAdmissionChecklistHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetAdmissionChecklistQuery, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(GetAdmissionChecklistQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return await Slots.ChecklistAsync(db, query.ApplicationId, cancellationToken);
    }
}

/// <summary>Brings an application's checklist in line with the current template. Items staff added or waived by hand are left alone.</summary>
public record RefreshChecklistCommand(Guid ApplicationId) : ICommand<ChecklistResult>;

public class RefreshChecklistHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<RefreshChecklistCommand, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(RefreshChecklistCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var application = await db.AdmissionApplications.FirstOrDefaultAsync(a => a.Id == command.ApplicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        if (!AdmissionWorkflow.IsActive(application.Status)) throw new DomainException("This application is finished, so its checklist can't change.");
        var template = (await Slots.ResolveTemplateAsync(db, application, cancellationToken)).Where(t => t.IsRequired).Select(t => t.DocumentTypeId).ToHashSet();
        var items = await db.ChecklistItems.Where(i => i.ApplicationId == application.Id && i.Stage == ChecklistStage.Admission).ToListAsync(cancellationToken);

        foreach (var typeId in template)
        {
            var item = items.FirstOrDefault(i => i.DocumentTypeId == typeId);
            if (item is null) db.ChecklistItems.Add(new ApplicationChecklistItem(application.Id, typeId, true, ChecklistSource.Template, null));
            else if (item.Source == ChecklistSource.Template) item.Set(true, ChecklistSource.Template, null);
        }

        foreach (var item in items.Where(i => i.Source == ChecklistSource.Template && !template.Contains(i.DocumentTypeId))) item.Set(false, ChecklistSource.Template, null);
        audit.Add(AuditAction.ChecklistChanged, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { change = "refreshed" });
        await db.SaveChangesAsync(cancellationToken);
        return await Slots.ChecklistAsync(db, application.Id, cancellationToken);
    }
}

/// <summary>Adds a document to one application's checklist, makes it required or optional, or waives it. Waiving needs a note.</summary>
public record SetChecklistItemCommand(Guid ApplicationId, Guid DocumentTypeId, bool Required, string? Note) : ICommand<ChecklistResult>;

public class SetChecklistItemValidator : AbstractValidator<SetChecklistItemCommand>
{
    public SetChecklistItemValidator() => RuleFor(x => x.Note).MaximumLength(1000);
}

public class SetChecklistItemHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : ICommandHandler<SetChecklistItemCommand, ChecklistResult>
{
    public async ValueTask<ChecklistResult> Handle(SetChecklistItemCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == command.ApplicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
        if (!AdmissionWorkflow.IsActive(application.Status)) throw new DomainException("This application is finished, so its checklist can't change.");
        var type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.DocumentTypeId && t.IsActive, cancellationToken) ?? throw new NotFoundException("Document type not found.");
        if (type.Purpose != DocumentPurpose.None) throw new DomainException("The system manages this document, so it isn't part of the checklist.");
        var item = await db.ChecklistItems.FirstOrDefaultAsync(i => i.ApplicationId == application.Id && i.Stage == ChecklistStage.Admission && i.DocumentTypeId == type.Id, cancellationToken);
        if (item is { IsRequired: true } && !command.Required && string.IsNullOrWhiteSpace(command.Note)) throw new DomainException("Say why this document is being waived.");

        if (item is null) db.ChecklistItems.Add(new ApplicationChecklistItem(application.Id, type.Id, command.Required, ChecklistSource.Manual, command.Note));
        else item.Set(command.Required, ChecklistSource.Manual, command.Note);
        audit.Add(AuditAction.ChecklistChanged, entityType: nameof(AdmissionApplication), entityId: application.Id.ToString(), details: new { change = command.Required ? "required" : "waived", type = type.Name });
        await db.SaveChangesAsync(cancellationToken);
        return await Slots.ChecklistAsync(db, application.Id, cancellationToken);
    }
}

// ─────────────────────────── Candidates' documents ───────────────────────────

public record GetMyDocumentsQuery : IQuery<DocumentsOverview>;

public class GetMyDocumentsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyDocumentsQuery, DocumentsOverview>
{
    public async ValueTask<DocumentsOverview> Handle(GetMyDocumentsQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) ?? throw new NotFoundException("Candidate profile not found.");
        return await Slots.OverviewAsync(db, profile.Id, cancellationToken);
    }
}

public record GetCandidateDocumentsQuery(Guid CandidateId) : IQuery<DocumentsOverview>;

public class GetCandidateDocumentsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetCandidateDocumentsQuery, DocumentsOverview>
{
    public async ValueTask<DocumentsOverview> Handle(GetCandidateDocumentsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        if (!await db.CandidateProfiles.AnyAsync(p => p.Id == query.CandidateId, cancellationToken)) throw new NotFoundException("Candidate not found.");
        return await Slots.OverviewAsync(db, query.CandidateId, cancellationToken);
    }
}

internal static class DocumentAccess
{
    /// <summary>The document, if the caller may see it: a candidate's own, or any case worker's.</summary>
    public static async Task<(CandidateDocument Document, bool IsOwner)> LoadAsync(IAppDbContext db, ICurrentUser current, Guid documentId, bool track, CancellationToken cancellationToken)
    {
        var query = track ? db.CandidateDocuments.AsQueryable() : db.CandidateDocuments.AsNoTracking();
        var document = await query.FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken) ?? throw new NotFoundException("Document not found.");
        if (current.UserId is { } userId && current.IsInRole(Roles.Candidate))
        {
            var ownerUserId = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == document.CandidateId).Select(p => p.UserId).FirstOrDefaultAsync(cancellationToken);
            if (ownerUserId != userId) throw new NotFoundException("Document not found.");
            return (document, true);
        }

        current.RequireAny(Guard.CaseWorkers);
        return (document, false);
    }
}

public record GetDocumentQuery(Guid DocumentId) : IQuery<DocumentDetail>;

public class GetDocumentHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetDocumentQuery, DocumentDetail>
{
    public async ValueTask<DocumentDetail> Handle(GetDocumentQuery query, CancellationToken cancellationToken)
    {
        var (document, isOwner) = await DocumentAccess.LoadAsync(db, current, query.DocumentId, track: false, cancellationToken);
        var typeName = await db.DocumentTypes.AsNoTracking().Where(t => t.Id == document.DocumentTypeId).Select(t => t.Name).FirstAsync(cancellationToken);
        var versions = await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == document.Id).OrderBy(v => v.VersionNumber).ToListAsync(cancellationToken);
        var reviews = await db.DocumentReviews.AsNoTracking().Where(r => r.DocumentId == document.Id).OrderBy(r => r.ReviewedAtUtc).ToListAsync(cancellationToken);
        // Candidates see what was decided and why, not which staff member decided or uploaded.
        return new DocumentDetail(document.Id, document.CandidateId, document.DocumentTypeId, typeName, document.ApplicationId, document.Status, document.CurrentVersion,
            versions.Select(v => new DocumentVersionResult(v.VersionNumber, v.FileName, v.ContentType, v.SizeBytes, v.UploadedAtUtc, isOwner ? null : v.UploadedByEmail, v.ExpiresOn, v.Position, v.RemovedAtUtc is not null)).ToList(),
            reviews.Select(r => new DocumentReviewResult(r.VersionNumber, r.Decision, r.Note, isOwner ? null : r.ReviewerEmail, r.ReviewedAtUtc)).ToList());
    }
}

// ─────────────────────────── Uploading ───────────────────────────

/// <summary>Files are checked by what they contain, not by the name or type the browser claims.</summary>
public static class UploadRules
{
    public const long MaxBytes = 10 * 1024 * 1024;

    private static readonly (string Extension, string ContentType, byte[] Magic)[] Allowed =
    [
        (".pdf", "application/pdf", "%PDF-"u8.ToArray()),
        (".jpg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
        (".jpeg", "image/jpeg", [0xFF, 0xD8, 0xFF]),
        (".png", "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
    ];

    public static string Check(string fileName, ReadOnlySpan<byte> content)
    {
        if (content.Length == 0) throw new DomainException("The file is empty.");
        if (content.Length > MaxBytes) throw new DomainException("The file is too large. The limit is 10 MB.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        foreach (var (ext, contentType, magic) in Allowed)
        {
            if (ext == extension && content.StartsWith(magic)) return contentType;
        }

        throw new DomainException("Upload a PDF, JPG or PNG file.");
    }

    public static string CleanName(string fileName)
    {
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > 150) name = name[^150..];
        return string.IsNullOrWhiteSpace(name) ? "document" : name;
    }
}

/// <summary>Uploads a file into a slot (or a new version of it). A candidate uploads for themselves; document staff can upload for a candidate.</summary>
/// <param name="Position">For a document that holds several files: which file this replaces. Leave it out to add another file.</param>
public record UploadDocumentCommand(Guid? CandidateId, Guid DocumentTypeId, Guid? ApplicationId, DateOnly? ExpiresOn, string FileName, Stream Content, int? Position = null) : ICommand<DocumentDetail>;

public class UploadDocumentHandler(
    IAppDbContext db,
    ICurrentUser current,
    IAuditLog audit,
    IClock clock,
    IObjectStorage storage,
    IEmailSender email,
    IAppUrls urls,
    Microsoft.Extensions.Options.IOptions<AdmissionNotificationOptions> notify,
    ILogger<UploadDocumentHandler> logger)
    : ICommandHandler<UploadDocumentCommand, DocumentDetail>
{
    public async ValueTask<DocumentDetail> Handle(UploadDocumentCommand command, CancellationToken cancellationToken)
    {
        if (current.UserId is not { } userId) throw new ForbiddenException();
        var byCandidate = current.IsInRole(Roles.Candidate);
        Guid candidateId;
        if (byCandidate)
        {
            candidateId = (await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken) ?? throw new NotFoundException("Candidate profile not found.")).Id;
            if (command.CandidateId is { } other && other != candidateId) throw new ForbiddenException();
        }
        else
        {
            current.RequireAny(Guard.DocumentStaff);
            candidateId = command.CandidateId ?? throw new DomainException("Say which candidate this is for.");
            if (!await db.CandidateProfiles.AnyAsync(p => p.Id == candidateId, cancellationToken)) throw new NotFoundException("Candidate not found.");
        }

        var type = await db.DocumentTypes.AsNoTracking().FirstOrDefaultAsync(t => t.Id == command.DocumentTypeId && t.IsActive, cancellationToken) ?? throw new NotFoundException("Document type not found.");
        if (type.IsAutomatic) throw new DomainException("This one is met when the application form is signed. Nothing to upload.");
        Guid? applicationId = null;
        if (type.Scope == DocumentScope.Application)
        {
            applicationId = command.ApplicationId ?? throw new DomainException("Say which application this document is for.");
            var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId && a.CandidateId == candidateId, cancellationToken) ?? throw new NotFoundException("Application not found.");
            if (!AdmissionWorkflow.IsActive(application.Status) && !await db.VisaCases.AnyAsync(v => v.ApplicationId == application.Id && v.Status != VisaStatus.Approved && v.Status != VisaStatus.Refused && v.Status != VisaStatus.Withdrawn, cancellationToken))
            {
                throw new DomainException("This application is finished.");
            }
        }
        else if (command.ApplicationId is not null)
        {
            throw new DomainException("This document is shared across applications, so it isn't tied to one.");
        }

        // The school's letters follow their own rules; everything else is a checklist document.
        if (type.Purpose != DocumentPurpose.None) await CheckSystemUploadAsync(type, applicationId!.Value, byCandidate, cancellationToken);

        // Candidates upload only what they've been asked for; staff may add anything.
        if (byCandidate && type.Purpose == DocumentPurpose.None)
        {
            var overview = await Slots.OverviewAsync(db, candidateId, cancellationToken);
            if (!overview.Slots.Any(s => s.DocumentTypeId == type.Id && s.ApplicationId == applicationId)) throw new DomainException("That document isn't on your list.");
        }

        if (command.ExpiresOn is { } expires)
        {
            if (!type.TracksExpiry) throw new DomainException("This kind of document doesn't have an expiry date.");
            if (expires < DateOnly.FromDateTime(clock.UtcNow)) throw new DomainException("This document has already expired.");
        }

        // The API caps the request size; the content is checked again below.
        using var buffer = new MemoryStream();
        await command.Content.CopyToAsync(buffer, 81920, cancellationToken);
        var bytes = buffer.GetBuffer().AsMemory(0, (int)buffer.Length);
        var contentType = UploadRules.Check(command.FileName, bytes.Span);
        var fileName = UploadRules.CleanName(command.FileName);
        var hash = Convert.ToHexString(SHA256.HashData(bytes.Span)).ToLowerInvariant();

        var document = await db.CandidateDocuments.FirstOrDefaultAsync(d => d.CandidateId == candidateId && d.DocumentTypeId == type.Id && d.ApplicationId == applicationId, cancellationToken);
        var isNew = document is null;
        document ??= new CandidateDocument(candidateId, type.Id, applicationId);
        // Staff sent this back for fixes, so this upload is the answer they are waiting for.
        var resubmission = byCandidate && !isNew && document.Status == DocumentStatus.NeedsResubmission;

        // Which file of the document this is. A single-file document is always file 1 (a new upload replaces it).
        var position = 1;
        if (type.AllowsMultipleFiles && type.Purpose == DocumentPurpose.None)
        {
            var existing = isNew ? [] : await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == document.Id).ToListAsync(cancellationToken);
            var live = DocumentFiles.Live(existing);
            if (command.Position is { } replacing)
            {
                if (!live.Any(f => f.Position == replacing)) throw new DomainException("That file is no longer in the list.");
                position = replacing;
            }
            else
            {
                if (live.Count >= CandidateDocument.MaxFiles) throw new DomainException($"You can add up to {CandidateDocument.MaxFiles} files to this document. Remove one first, or replace one.");
                position = existing.Count == 0 ? 1 : existing.Max(v => v.Position) + 1;
            }
        }
        else if (command.Position is not null and not 1)
        {
            throw new DomainException("This document is a single file.");
        }

        var key = $"candidates/{candidateId}/{document.Id}/v{document.CurrentVersion + 1}-{Guid.NewGuid():N}";
        var version = document.NewVersion(key, fileName, contentType, bytes.Length, hash, command.ExpiresOn, userId, current.Email, clock.UtcNow, byCandidate, position);

        await storage.PutAsync(key, new MemoryStream(buffer.GetBuffer(), 0, (int)buffer.Length, writable: false), contentType, cancellationToken);
        try
        {
            if (isNew) db.CandidateDocuments.Add(document);
            db.DocumentVersions.Add(version);
            audit.Add(AuditAction.DocumentUploaded, entityType: nameof(CandidateDocument), entityId: document.Id.ToString(), details: new { type = type.Name, version = version.VersionNumber, file = position, bytes = bytes.Length });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Don't leave an orphaned file behind if the record couldn't be saved.
            try { await storage.DeleteAsync(key, CancellationToken.None); }
            catch (Exception ex) { logger.LogWarning(ex, "Couldn't remove an orphaned upload"); }
            throw;
        }

        if (resubmission && !string.IsNullOrWhiteSpace(notify.Value.NotifyEmail))
        {
            // Only the first upload after a document is sent back finds it in that state, so this goes out once per round, however many files follow.
            var student = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == candidateId).Select(p => p.FirstName + " " + p.LastName).FirstAsync(cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.DocumentResubmitted(notify.Value.NotifyEmail.Trim(), student, type.Name, $"{urls.WebBaseUrl}/staff/candidates/{candidateId}"), logger, cancellationToken);
        }

        // A student's offer letter is news for staff, who then record the offer. A link only, like every email here.
        if (byCandidate && type.Purpose == DocumentPurpose.OfferLetter && !string.IsNullOrWhiteSpace(notify.Value.NotifyEmail))
        {
            var student = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == candidateId).Select(p => p.FirstName + " " + p.LastName).FirstAsync(cancellationToken);
            var school = await db.AdmissionApplications.AsNoTracking().Where(a => a.Id == applicationId).Join(db.Institutions, a => a.InstitutionId, i => i.Id, (a, i) => i.Name).FirstAsync(cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.OfferLetterUploaded(notify.Value.NotifyEmail.Trim(), student, school, $"{urls.WebBaseUrl}/staff/admissions/{applicationId}"), logger, cancellationToken);
        }

        return await new GetDocumentHandler(db, current).Handle(new GetDocumentQuery(document.Id), cancellationToken);
    }

    /// <summary>What may be uploaded as one of the school's or the visa authority's letters. The student adds the offer letter; the acceptance and PAL letters are staff's.</summary>
    private async Task CheckSystemUploadAsync(DocumentType type, Guid applicationId, bool byCandidate, CancellationToken cancellationToken)
    {
        var application = await db.AdmissionApplications.AsNoTracking().FirstAsync(a => a.Id == applicationId, cancellationToken);
        if (type.Purpose is DocumentPurpose.BiometricsLetter or DocumentPurpose.DecisionLetter)
        {
            // Visa letters belong to the visa case. The student can add the biometrics letter they were sent; the decision is staff's.
            var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.ApplicationId == applicationId, cancellationToken) ?? throw new DomainException("There is no visa case for this application yet.");
            if (type.Purpose == DocumentPurpose.BiometricsLetter)
            {
                if (!VisaWorkflow.AcceptsBiometricsLetter(visa.Status)) throw new DomainException("You can add the biometrics letter once the visa application has been submitted.");
                return;
            }

            if (byCandidate) throw new ForbiddenException();
            if (visa.Status is not (VisaStatus.Approved or VisaStatus.Refused)) throw new DomainException("Record the visa decision first.");
            return;
        }

        if (type.Purpose == DocumentPurpose.OfferLetter)
        {
            // The student (or staff for them) uploads the offer letter once the application has gone to the school.
            if (!AdmissionWorkflow.AcceptsOfferLetter(application.Status)) throw new DomainException("You can add the offer letter once your counsellor has applied to the school.");
            return;
        }

        // The acceptance and PAL letters are still staff's to add.
        if (byCandidate) throw new ForbiddenException();

        if (!await db.AdmissionAcceptances.AnyAsync(a => a.ApplicationId == applicationId, cancellationToken)) throw new DomainException("Record the formal acceptance first.");
        if (type.Purpose == DocumentPurpose.PalLetter && !PalRules.Applies(application.Country, application.Level)) throw new DomainException("A PAL letter only goes with a Canadian undergraduate acceptance.");
    }
}

// ─────────────────────────── Reviewing and downloading ───────────────────────────

public record ReviewDocumentCommand(Guid DocumentId, DocumentStatus Decision, string? Note) : ICommand<DocumentDetail>;

public class ReviewDocumentValidator : AbstractValidator<ReviewDocumentCommand>
{
    public ReviewDocumentValidator() => RuleFor(x => x.Note).MaximumLength(2000);
}

public class ReviewDocumentHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock, IEmailSender email, IAppUrls urls, ILogger<ReviewDocumentHandler> logger)
    : ICommandHandler<ReviewDocumentCommand, DocumentDetail>
{
    public async ValueTask<DocumentDetail> Handle(ReviewDocumentCommand command, CancellationToken cancellationToken)
    {
        var reviewerId = current.RequireAny(Guard.DocumentStaff);
        var document = await db.CandidateDocuments.FirstOrDefaultAsync(d => d.Id == command.DocumentId, cancellationToken) ?? throw new NotFoundException("Document not found.");
        if (await db.DocumentTypes.AsNoTracking().AnyAsync(t => t.Id == document.DocumentTypeId && t.Purpose != DocumentPurpose.None, cancellationToken)) throw new DomainException("This one isn't reviewed here. Offers and the school's letters are handled on the application.");
        if ((await Slots.VisaOnlyTypeIdsAsync(db, cancellationToken)).Contains(document.DocumentTypeId))
        {
            throw new DomainException("Visa documents aren't reviewed here. They are collected so the visa application can be submitted on the visa platform.");
        }

        if (!DocumentFiles.Live(await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == document.Id).ToListAsync(cancellationToken)).Any()) throw new DomainException("There is no file to review.");
        var review = document.Review(command.Decision, command.Note, reviewerId, current.Email, clock.UtcNow);
        db.DocumentReviews.Add(review);
        audit.Add(AuditAction.DocumentReviewed, entityType: nameof(CandidateDocument), entityId: document.Id.ToString(), details: new { decision = command.Decision.ToString(), version = review.VersionNumber });
        await db.SaveChangesAsync(cancellationToken);

        if (command.Decision == DocumentStatus.NeedsResubmission)
        {
            // A link only; what to fix is shown after signing in.
            var profile = await db.CandidateProfiles.AsNoTracking().FirstAsync(p => p.Id == document.CandidateId, cancellationToken);
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.DocumentNeedsAttention(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/documents"), logger, cancellationToken);
        }

        return await new GetDocumentHandler(db, current).Handle(new GetDocumentQuery(document.Id), cancellationToken);
    }
}

public record DownloadDocumentQuery(Guid DocumentId, int? VersionNumber) : IQuery<FileDownload>;

public class DownloadDocumentHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IObjectStorage storage) : IQueryHandler<DownloadDocumentQuery, FileDownload>
{
    public async ValueTask<FileDownload> Handle(DownloadDocumentQuery query, CancellationToken cancellationToken)
    {
        var (document, isOwner) = await DocumentAccess.LoadAsync(db, current, query.DocumentId, track: false, cancellationToken);
        // With no version asked for, the most recent file still in the document.
        var number = query.VersionNumber ?? DocumentFiles.Live(await db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == document.Id).ToListAsync(cancellationToken)).Select(v => v.VersionNumber).DefaultIfEmpty(document.CurrentVersion).Max();
        var version = await db.DocumentVersions.AsNoTracking().FirstOrDefaultAsync(v => v.DocumentId == document.Id && v.VersionNumber == number, cancellationToken) ?? throw new NotFoundException("Version not found.");
        // A file taken out of the set stays on record for staff, but the student no longer sees it.
        if (isOwner && version.RemovedAtUtc is not null) throw new NotFoundException("Version not found.");
        audit.Add(AuditAction.DocumentDownloaded, entityType: nameof(CandidateDocument), entityId: document.Id.ToString(), details: new { version = number });
        await db.SaveChangesAsync(cancellationToken);
        return new FileDownload(await storage.GetAsync(version.StorageKey, cancellationToken), version.FileName, version.ContentType);
    }
}

/// <summary>Takes one file out of a document that holds several (a wrong upload). The file stays on record; it just stops counting.</summary>
public record RemoveDocumentFileCommand(Guid DocumentId, int Position) : ICommand<DocumentDetail>;

public class RemoveDocumentFileHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<RemoveDocumentFileCommand, DocumentDetail>
{
    public async ValueTask<DocumentDetail> Handle(RemoveDocumentFileCommand command, CancellationToken cancellationToken)
    {
        var (document, isOwner) = await DocumentAccess.LoadAsync(db, current, command.DocumentId, track: true, cancellationToken);
        var type = await db.DocumentTypes.AsNoTracking().FirstAsync(t => t.Id == document.DocumentTypeId, cancellationToken);
        if (!type.AllowsMultipleFiles || type.Purpose != DocumentPurpose.None) throw new DomainException("This document is a single file. Replace it instead.");
        if (isOwner && document.Status == DocumentStatus.Accepted) throw new DomainException("This document has been accepted. Ask your counsellor if it needs changing.");

        var versions = await db.DocumentVersions.Where(v => v.DocumentId == document.Id && v.Position == command.Position && v.RemovedAtUtc == null).ToListAsync(cancellationToken);
        if (versions.Count == 0) throw new NotFoundException("That file is no longer in the list.");
        foreach (var v in versions) v.Remove(clock.UtcNow);
        audit.Add(AuditAction.DocumentFileRemoved, entityType: nameof(CandidateDocument), entityId: document.Id.ToString(), details: new { type = type.Name, file = command.Position });
        await db.SaveChangesAsync(cancellationToken);
        return await new GetDocumentHandler(db, current).Handle(new GetDocumentQuery(document.Id), cancellationToken);
    }
}

public record ListReviewQueueQuery(DocumentStatus? Status, int Page = 1, int PageSize = 25) : IQuery<PagedResult<ReviewQueueItem>>;

public class ListReviewQueueValidator : AbstractValidator<ListReviewQueueQuery>
{
    public ListReviewQueueValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class ListReviewQueueHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListReviewQueueQuery, PagedResult<ReviewQueueItem>>
{
    public async ValueTask<PagedResult<ReviewQueueItem>> Handle(ListReviewQueueQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        // Waiting to be looked at, unless a particular status is asked for.
        var visaOnly = (await Slots.VisaOnlyTypeIdsAsync(db, cancellationToken)).ToList();
        var documents = db.CandidateDocuments.AsNoTracking().Where(d => d.CurrentVersion > 0 && !visaOnly.Contains(d.DocumentTypeId) && db.DocumentVersions.Any(v => v.DocumentId == d.Id && v.RemovedAtUtc == null));
        documents = query.Status is { } status ? documents.Where(d => d.Status == status) : documents.Where(d => d.Status == DocumentStatus.Uploaded || d.Status == DocumentStatus.UnderReview);

        var rows = from d in documents
                   join c in db.CandidateProfiles on d.CandidateId equals c.Id
                   join t in db.DocumentTypes on d.DocumentTypeId equals t.Id
                   join v in db.DocumentVersions on new { Id = d.Id, N = d.CurrentVersion } equals new { Id = v.DocumentId, N = v.VersionNumber }
                   where t.Purpose == DocumentPurpose.None
                   orderby v.UploadedAtUtc
                   select new { d.Id, d.CandidateId, c.FirstName, c.LastName, TypeName = t.Name, d.Status, d.CurrentVersion, v.FileName, v.UploadedAtUtc };
        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        var ids = page.Select(r => r.Id).ToList();
        var liveByDocument = (await db.DocumentVersions.AsNoTracking().Where(v => ids.Contains(v.DocumentId)).ToListAsync(cancellationToken)).GroupBy(v => v.DocumentId).ToDictionary(g => g.Key, g => DocumentFiles.Live(g));
        return new PagedResult<ReviewQueueItem>(page.Select(r =>
        {
            var live = liveByDocument.GetValueOrDefault(r.Id) ?? [];
            var latest = live.OrderByDescending(f => f.VersionNumber).FirstOrDefault();
            return new ReviewQueueItem(r.Id, r.CandidateId, $"{r.FirstName} {r.LastName}", r.TypeName, r.Status, r.CurrentVersion, live.Count > 1 ? $"{live.Count} files" : latest?.FileName ?? r.FileName, latest?.UploadedAtUtc ?? r.UploadedAtUtc, Math.Max(1, live.Count));
        }).ToList(), query.Page, query.PageSize, total);
    }
}
