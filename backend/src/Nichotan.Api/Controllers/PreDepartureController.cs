using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Visas;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Visas;

namespace Nichotan.Api.Controllers;

public record AddPreDepartureItemRequest(string Title, string? Detail, PreDepartureImportance Importance = PreDepartureImportance.Required);
public record UpdatePreDepartureItemRequest(string? Note, PreDepartureImportance Importance, bool IsActive = true);
public record VerifyPreDepartureItemRequest(bool Verified);
public record ConfirmPreDepartureItemRequest(bool Confirmed);
public record DepartureDateRequest(DateOnly? PlannedOn);
public record SavePreDepartureTemplateItemRequest(DestinationCountry Country, string Title, string? Detail, PreDepartureImportance Importance, int SortOrder, bool IsActive = true);
public record SavePreDepartureCaveatRequest(string Text);

/// <summary>
/// The pre-departure ("To Carry") list for an approved visa. Staff activate it from the country's template; the student confirms
/// each item and staff verify it.
/// </summary>
[ApiController]
public class PreDepartureController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/visa-cases/{id:guid}/pre-departure")]
    public async Task<PreDepartureResult> Get(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetPreDepartureQuery(id), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost("api/visa-cases/{id:guid}/pre-departure")]
    public async Task<PreDepartureResult> Activate(Guid id, CancellationToken cancellationToken) => await sender.Send(new ActivatePreDepartureCommand(id), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost("api/visa-cases/{id:guid}/pre-departure/items")]
    public async Task<PreDepartureResult> AddItem(Guid id, AddPreDepartureItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new AddPreDepartureItemCommand(id, request.Title, request.Detail, request.Importance), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPut("api/visa-cases/{id:guid}/pre-departure/items/{itemId:guid}")]
    public async Task<PreDepartureResult> UpdateItem(Guid id, Guid itemId, UpdatePreDepartureItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdatePreDepartureItemCommand(id, itemId, request.Note, request.Importance, request.IsActive), cancellationToken);

    [Authorize(Policy = Policies.VisaStaff)]
    [HttpPost("api/visa-cases/{id:guid}/pre-departure/items/{itemId:guid}/verify")]
    public async Task<PreDepartureResult> Verify(Guid id, Guid itemId, VerifyPreDepartureItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new VerifyPreDepartureItemCommand(id, itemId, request.Verified), cancellationToken);

    [Authorize(Policy = Policies.CandidateOrStaff)]
    [HttpPut("api/visa-cases/{id:guid}/pre-departure/departure")]
    public async Task<PreDepartureResult> SetDeparture(Guid id, DepartureDateRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SetDepartureDateCommand(id, request.PlannedOn), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("api/visa-cases/mine/pre-departure")]
    public async Task<IReadOnlyList<PreDepartureResult>> Mine(CancellationToken cancellationToken) => await sender.Send(new GetMyPreDepartureQuery(), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpPut("api/visa-cases/mine/pre-departure/items/{itemId:guid}/confirm")]
    public async Task<PreDepartureResult> Confirm(Guid itemId, ConfirmPreDepartureItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ConfirmMyPreDepartureItemCommand(itemId, request.Confirmed), cancellationToken);

    // ── The country templates ──

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/pre-departure-template")]
    public async Task<PreDepartureTemplateResult> Template(CancellationToken cancellationToken) => await sender.Send(new GetPreDepartureTemplateQuery(), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPost("api/pre-departure-template/items")]
    public async Task<PreDepartureTemplateItemResult> CreateTemplateItem(SavePreDepartureTemplateItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SavePreDepartureTemplateItemCommand(null, request.Country, request.Title, request.Detail, request.Importance, request.SortOrder, request.IsActive), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPut("api/pre-departure-template/items/{id:guid}")]
    public async Task<PreDepartureTemplateItemResult> UpdateTemplateItem(Guid id, SavePreDepartureTemplateItemRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SavePreDepartureTemplateItemCommand(id, request.Country, request.Title, request.Detail, request.Importance, request.SortOrder, request.IsActive), cancellationToken);

    [Authorize(Policy = Policies.Administrator)]
    [HttpPut("api/pre-departure-template/caveats/{country}")]
    public async Task<PreDepartureCaveatResult> SaveCaveat(DestinationCountry country, SavePreDepartureCaveatRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new SavePreDepartureCaveatCommand(country, request.Text), cancellationToken);
}
