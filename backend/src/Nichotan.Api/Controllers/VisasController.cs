using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Candidates;
using Nichotan.Application.Documents;
using Nichotan.Application.Visas;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Visas;

namespace Nichotan.Api.Controllers;

public record OpenVisaRequest(Guid ApplicationId);
public record ChangeVisaStatusRequest(VisaStatus ToStatus, DateOnly? OccurredOn, string? Note, string? ReferenceNumber, DateOnly? BiometricsAppointmentOn, string? BiometricsLocation, bool AcknowledgeDocumentGaps = false);
public record CorrectVisaStatusRequest(VisaStatus ToStatus, DateOnly? OccurredOn, string Note);
public record VisaChecklistItemRequest(Guid DocumentTypeId, bool Required, string? Note);
public record BiometricsDoneRequest(DateOnly? DoneOn);
public record VisaDetailsRequest(string? ReferenceNumber, DateOnly? BiometricsAppointmentOn, string? BiometricsLocation);

/// <summary>Visa cases. Statuses are entered by staff from the immigration authority's own system; nothing syncs.</summary>
[ApiController]
[Route("api/visa-cases")]
public class VisasController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<PagedResult<VisaSummary>> List([FromQuery] VisaStatus? status, [FromQuery] DestinationCountry? country, [FromQuery] Guid? applicationId, [FromQuery] Guid? candidateId, [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListVisasQuery(status, country, applicationId, candidateId, search, page, pageSize), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("stats")]
    public async Task<VisaStats> Stats(CancellationToken cancellationToken) => await sender.Send(new GetVisaStatsQuery(), cancellationToken);

    /// <summary>Visa cases that have been lodged but whose biometrics aren't marked done: the staff reminder.</summary>
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("biometrics-follow-ups")]
    public async Task<IReadOnlyList<BiometricsFollowUp>> BiometricsFollowUps(CancellationToken cancellationToken) => await sender.Send(new ListBiometricsFollowUpsQuery(), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpPost("mine/{id:guid}/biometrics-done")]
    public async Task<MyVisaStep> MarkBiometricsDone(Guid id, BiometricsDoneRequest request, CancellationToken cancellationToken) => await sender.Send(new MarkMyBiometricsDoneCommand(id, request.DoneOn), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("mine")]
    public async Task<IReadOnlyList<MyVisaResult>> Mine(CancellationToken cancellationToken) => await sender.Send(new GetMyVisasQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("{id:guid}")]
    public async Task<VisaDetail> Get(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetVisaQuery(id), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost]
    public async Task<VisaDetail> Open(OpenVisaRequest request, CancellationToken cancellationToken) => await sender.Send(new OpenVisaCaseCommand(request.ApplicationId), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost("{id:guid}/status")]
    public async Task<VisaDetail> ChangeStatus(Guid id, ChangeVisaStatusRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ChangeVisaStatusCommand(id, request.ToStatus, request.OccurredOn, request.Note, request.ReferenceNumber, request.BiometricsAppointmentOn, request.BiometricsLocation, request.AcknowledgeDocumentGaps), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPost("{id:guid}/correct")]
    public async Task<VisaDetail> Correct(Guid id, CorrectVisaStatusRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new CorrectVisaStatusCommand(id, request.ToStatus, request.OccurredOn, request.Note), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPut("{id:guid}/details")]
    public async Task<VisaDetail> Details(Guid id, VisaDetailsRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateVisaDetailsCommand(id, request.ReferenceNumber, request.BiometricsAppointmentOn, request.BiometricsLocation), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("{id:guid}/checklist")]
    public async Task<ChecklistResult> Checklist(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetVisaChecklistQuery(id), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost("{id:guid}/checklist/refresh")]
    public async Task<ChecklistResult> RefreshChecklist(Guid id, CancellationToken cancellationToken) => await sender.Send(new RefreshVisaChecklistCommand(id), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPut("{id:guid}/checklist/items")]
    public async Task<ChecklistResult> SetChecklistItem(Guid id, VisaChecklistItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetVisaChecklistItemCommand(id, request.DocumentTypeId, request.Required, request.Note), cancellationToken);
}
