using System.Text.RegularExpressions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Tasks;

namespace Nichotan.Application.Tasks;

/// <summary>
/// One line on a student's timeline. <paramref name="AtUtc"/> is when it was recorded in the system, which is what orders the list;
/// where staff dated a move to an earlier day, <paramref name="OccurredOn"/> says which day it belongs to.
/// </summary>
public record TimelineEntry(DateTime AtUtc, DateOnly? OccurredOn, string Kind, string Title, string? Detail, string? By, string? LinkType, Guid? LinkId);

/// <summary>
/// Everything that has happened on a student's record, newest first: registration, the form, admission moves, offers and acceptance,
/// visa moves, the pre-departure list, documents uploaded and reviewed, and tasks. Built from the records already kept: nothing is
/// stored twice. Messages will join it when messaging exists.
/// </summary>
public record GetCandidateTimelineQuery(Guid CandidateId) : IQuery<IReadOnlyList<TimelineEntry>>;

public partial class GetCandidateTimelineHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : IQueryHandler<GetCandidateTimelineQuery, IReadOnlyList<TimelineEntry>>
{
    private const int Limit = 300;

    public async ValueTask<IReadOnlyList<TimelineEntry>> Handle(GetCandidateTimelineQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var id = query.CandidateId;
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken) ?? throw new NotFoundException("Student not found.");
        var entries = new List<TimelineEntry> { new(profile.CreatedAtUtc, null, "Account", "Registered", null, null, null, null) };

        // The form.
        var form = await db.ApplicationForms.AsNoTracking().FirstOrDefaultAsync(f => f.CandidateId == id, cancellationToken);
        if (form is not null)
        {
            var versions = await db.ApplicationFormVersions.AsNoTracking().Where(v => v.FormId == form.Id).Select(v => new { v.VersionNumber, v.SubmittedAtUtc }).ToListAsync(cancellationToken);
            entries.AddRange(versions.Select(v => new TimelineEntry(v.SubmittedAtUtc, null, "Form", $"Application form signed and submitted (version {v.VersionNumber})", null, null, null, null)));
            if (form.ReopenedAtUtc is { } reopened) entries.Add(new TimelineEntry(reopened, null, "Form", "Application form reopened", form.ReopenReason, null, null, null));
        }

        // Admission applications: each move, and the offer and acceptance records.
        var applications = await (from a in db.AdmissionApplications.AsNoTracking()
                                  join i in db.Institutions.AsNoTracking() on a.InstitutionId equals i.Id
                                  where a.CandidateId == id
                                  select new { a.Id, School = i.Name }).ToListAsync(cancellationToken);
        var applicationIds = applications.Select(a => a.Id).ToList();
        var school = applications.ToDictionary(a => a.Id, a => a.School);
        var moves = await db.AdmissionStatusEvents.AsNoTracking().Where(e => applicationIds.Contains(e.ApplicationId)).ToListAsync(cancellationToken);
        entries.AddRange(moves.Select(e => new TimelineEntry(e.RecordedAtUtc, e.OccurredOn, "Admission", $"{school[e.ApplicationId]}: {Words(e.To.ToString())}{(e.IsCorrection ? " (correction)" : string.Empty)}", e.Note, e.RecordedByEmail, "admission", e.ApplicationId)));
        var offers = await db.AdmissionOffers.AsNoTracking().Where(o => applicationIds.Contains(o.ApplicationId)).ToListAsync(cancellationToken);
        entries.AddRange(offers.Select(o => new TimelineEntry(o.CreatedAtUtc, null, "Admission", $"{school[o.ApplicationId]}: offer details recorded", null, o.RecordedByEmail, "admission", o.ApplicationId)));
        var acceptances = await db.AdmissionAcceptances.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId)).ToListAsync(cancellationToken);
        entries.AddRange(acceptances.Select(a => new TimelineEntry(a.CreatedAtUtc, a.ReceivedOn, "Admission", $"{school[a.ApplicationId]}: formal acceptance recorded{(a.PalNumber is null ? string.Empty : " with a PAL number")}", null, a.RecordedByEmail, "admission", a.ApplicationId)));

        // Visa cases and their pre-departure lists.
        var visas = await db.VisaCases.AsNoTracking().Where(v => v.CandidateId == id).Select(v => new { v.Id, v.ApplicationId }).ToListAsync(cancellationToken);
        var visaIds = visas.Select(v => v.Id).ToList();
        var visaMoves = await db.VisaStatusEvents.AsNoTracking().Where(e => visaIds.Contains(e.VisaCaseId)).ToListAsync(cancellationToken);
        entries.AddRange(visaMoves.Select(e => new TimelineEntry(e.RecordedAtUtc, e.OccurredOn, "Visa", e.From is null ? "Visa case opened" : $"Visa: {Words(e.To.ToString())}{(e.IsCorrection ? " (correction)" : string.Empty)}", e.From is null ? null : e.Note, e.RecordedByEmail, "visa", e.VisaCaseId)));
        var lists = await db.PreDepartureLists.AsNoTracking().Where(l => visaIds.Contains(l.VisaCaseId)).ToListAsync(cancellationToken);
        entries.AddRange(lists.Select(l => new TimelineEntry(l.CreatedAtUtc, l.ActivatedOn, "Visa", "Pre-departure list opened", null, l.ActivatedByEmail, "visa", l.VisaCaseId)));

        // Documents: each upload and each review decision.
        var documents = await (from d in db.CandidateDocuments.AsNoTracking()
                               join t in db.DocumentTypes.AsNoTracking() on d.DocumentTypeId equals t.Id
                               where d.CandidateId == id
                               select new { d.Id, Type = t.Name }).ToListAsync(cancellationToken);
        var documentIds = documents.Select(d => d.Id).ToList();
        var type = documents.ToDictionary(d => d.Id, d => d.Type);
        var uploads = await db.DocumentVersions.AsNoTracking().Where(v => documentIds.Contains(v.DocumentId)).Select(v => new { v.DocumentId, v.VersionNumber, v.UploadedAtUtc, v.UploadedByEmail }).ToListAsync(cancellationToken);
        entries.AddRange(uploads.Select(v => new TimelineEntry(v.UploadedAtUtc, null, "Document", $"{type[v.DocumentId]} uploaded{(v.VersionNumber > 1 ? $" (version {v.VersionNumber})" : string.Empty)}", null, v.UploadedByEmail, "documents", null)));
        var reviews = await db.DocumentReviews.AsNoTracking().Where(r => documentIds.Contains(r.DocumentId)).Select(r => new { r.DocumentId, r.Decision, r.Note, r.ReviewerEmail, r.ReviewedAtUtc }).ToListAsync(cancellationToken);
        entries.AddRange(reviews.Select(r => new TimelineEntry(r.ReviewedAtUtc, null, "Document", $"{type[r.DocumentId]}: {Words(r.Decision.ToString()).ToLowerInvariant()}", r.Note, r.ReviewerEmail, "documents", null)));

        // Tasks.
        var tasks = await db.StaffTasks.AsNoTracking().Where(t => t.CandidateId == id).ToListAsync(cancellationToken);
        entries.AddRange(tasks.Select(t => new TimelineEntry(t.CreatedAtUtc, null, "Task", $"Task added: {t.Title}", null, t.CreatedByEmail, "task", t.Id)));
        entries.AddRange(tasks.Where(t => t.CompletedAtUtc is not null).Select(t => new TimelineEntry(t.CompletedAtUtc!.Value, null, "Task", $"Task {(t.Status == StaffTaskStatus.Done ? "done" : "cancelled")}: {t.Title}", null, t.CompletedByEmail, "task", t.Id)));

        // Opening someone's whole history is audited, like opening their form.
        audit.Add(AuditAction.CandidateTimelineViewed, entityType: nameof(Domain.Candidates.CandidateProfile), entityId: id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return entries.OrderByDescending(e => e.AtUtc).Take(Limit).ToList();
    }

    /// <summary>"OfferReceived" becomes "Offer received".</summary>
    private static string Words(string name) => WordBreak().Replace(name, " $1").Trim().Replace("  ", " ") is { Length: > 0 } s ? char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant() : name;

    [GeneratedRegex("(?<!^)([A-Z])")]
    private static partial Regex WordBreak();
}
