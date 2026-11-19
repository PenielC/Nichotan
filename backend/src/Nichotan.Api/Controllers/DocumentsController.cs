using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Candidates;
using Nichotan.Application.Documents;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Documents;

namespace Nichotan.Api.Controllers;

public record SaveDocumentTypeRequest(string Name, string? Description, DocumentScope Scope, bool TracksExpiry, bool IsActive = true, bool AllowsMultipleFiles = false);
public record SaveRequirementRequest(Guid DocumentTypeId, DestinationCountry? Country, ApplicantLevel? Level, Guid? InstitutionId, bool IsRequired, bool IsActive = true, ChecklistStage Stage = ChecklistStage.Admission);
public record ReviewDocumentRequest(DocumentStatus Decision, string? Note);
public record ChecklistItemRequest(Guid DocumentTypeId, bool Required, string? Note);

/// <summary>The upload form. Files go through this API, never straight to storage.</summary>
public class UploadDocumentForm
{
    public Guid DocumentTypeId { get; set; }
    public Guid? ApplicationId { get; set; }

    /// <summary>Staff only: whose document this is. A candidate's own uploads ignore it.</summary>
    public Guid? CandidateId { get; set; }

    public DateOnly? ExpiresOn { get; set; }

    /// <summary>For a document that holds several files: which file this replaces. Leave it out to add another.</summary>
    public int? Position { get; set; }

    public IFormFile? File { get; set; }
}

/// <summary>Document types and checklist template (administrators), checklists, uploads, review and downloads.</summary>
[ApiController]
[Route("api")]
public class DocumentsController(ISender sender) : ControllerBase
{
    private const long RequestLimit = 11 * 1024 * 1024;

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("document-types")]
    public async Task<IReadOnlyList<DocumentTypeResult>> Types(CancellationToken cancellationToken) => await sender.Send(new ListDocumentTypesQuery(), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPost("document-types")]
    public async Task<DocumentTypeResult> CreateType(SaveDocumentTypeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveDocumentTypeCommand(null, request.Name, request.Description, request.Scope, request.TracksExpiry, request.IsActive, request.AllowsMultipleFiles), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPut("document-types/{id:guid}")]
    public async Task<DocumentTypeResult> UpdateType(Guid id, SaveDocumentTypeRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveDocumentTypeCommand(id, request.Name, request.Description, request.Scope, request.TracksExpiry, request.IsActive, request.AllowsMultipleFiles), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("document-requirements")]
    public async Task<IReadOnlyList<RequirementResult>> Requirements(CancellationToken cancellationToken) => await sender.Send(new ListRequirementsQuery(), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPost("document-requirements")]
    public async Task<RequirementResult> CreateRequirement(SaveRequirementRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveRequirementCommand(null, request.DocumentTypeId, request.Country, request.Level, request.InstitutionId, request.IsRequired, request.IsActive, request.Stage), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPut("document-requirements/{id:guid}")]
    public async Task<RequirementResult> UpdateRequirement(Guid id, SaveRequirementRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SaveRequirementCommand(id, request.DocumentTypeId, request.Country, request.Level, request.InstitutionId, request.IsRequired, request.IsActive, request.Stage), cancellationToken);

    // ── An application's checklist ──

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("admissions/{applicationId:guid}/checklist")]
    public async Task<ChecklistResult> Checklist(Guid applicationId, CancellationToken cancellationToken) => await sender.Send(new GetAdmissionChecklistQuery(applicationId), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("admissions/{applicationId:guid}/checklist/refresh")]
    public async Task<ChecklistResult> Refresh(Guid applicationId, CancellationToken cancellationToken) => await sender.Send(new RefreshChecklistCommand(applicationId), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("admissions/{applicationId:guid}/checklist/items")]
    public async Task<ChecklistResult> SetItem(Guid applicationId, ChecklistItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetChecklistItemCommand(applicationId, request.DocumentTypeId, request.Required, request.Note), cancellationToken);

    // ── Candidates' documents ──

    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("documents/mine")]
    public async Task<DocumentsOverview> Mine(CancellationToken cancellationToken) => await sender.Send(new GetMyDocumentsQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("documents/candidate/{candidateId:guid}")]
    public async Task<DocumentsOverview> ForCandidate(Guid candidateId, CancellationToken cancellationToken) => await sender.Send(new GetCandidateDocumentsQuery(candidateId), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("documents/review-queue")]
    public async Task<PagedResult<ReviewQueueItem>> Queue([FromQuery] DocumentStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListReviewQueueQuery(status, page, pageSize), cancellationToken);

    [Authorize(Policy = Policies.CandidateOrStaff)]
    [HttpGet("documents/{id:guid}")]
    public async Task<DocumentDetail> Get(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetDocumentQuery(id), cancellationToken);

    /// <summary>Uploads a file as a new version of a document slot. PDF, JPG or PNG, up to 10 MB.</summary>
    [Authorize(Policy = Policies.CandidateOrStaff)]
    [HttpPost("documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(RequestLimit)]
    public async Task<DocumentDetail> Upload([FromForm] UploadDocumentForm form, CancellationToken cancellationToken)
    {
        if (form.File is null || form.File.Length == 0) throw new Nichotan.Domain.Common.DomainException("Choose a file to upload.");
        await using var content = form.File.OpenReadStream();
        return await sender.Send(new UploadDocumentCommand(form.CandidateId, form.DocumentTypeId, form.ApplicationId, form.ExpiresOn, form.File.FileName, content, form.Position), cancellationToken);
    }

    /// <summary>Takes one file out of a document that holds several. The file stays on record.</summary>
    [Authorize(Policy = Policies.CandidateOrStaff)]
    [HttpDelete("documents/{id:guid}/files/{position:int}")]
    public async Task<DocumentDetail> RemoveFile(Guid id, int position, CancellationToken cancellationToken) => await sender.Send(new RemoveDocumentFileCommand(id, position), cancellationToken);

    [Authorize(Policy = Policies.DocumentStaff)]
    [HttpPost("documents/{id:guid}/review")]
    public async Task<DocumentDetail> Review(Guid id, ReviewDocumentRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ReviewDocumentCommand(id, request.Decision, request.Note), cancellationToken);

    /// <summary>Streams a stored file after the access check. Storage itself is never exposed.</summary>
    [Authorize(Policy = Policies.CandidateOrStaff)]
    [HttpGet("documents/{id:guid}/file")]
    public async Task<IActionResult> File(Guid id, [FromQuery] int? version, CancellationToken cancellationToken)
    {
        var file = await sender.Send(new DownloadDocumentQuery(id, version), cancellationToken);
        return base.File(file.Content, file.ContentType, file.FileName);
    }
}
