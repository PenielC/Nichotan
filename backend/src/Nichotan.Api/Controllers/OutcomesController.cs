using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Admissions;

namespace Nichotan.Api.Controllers;

public record OfferRequest(string? Conditions, DateOnly? RespondByOn);
public record AcceptanceRequest(DateOnly ReceivedOn, string? PalNumber, string? Note);

/// <summary>The school's offer and the formal acceptance (with the PAL number) for one application. Staff record them from what the school sends.</summary>
[ApiController]
[Route("api/admissions/{applicationId:guid}/outcome")]
public class OutcomesController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<OutcomeResult> Get(Guid applicationId, CancellationToken cancellationToken) => await sender.Send(new GetOutcomeQuery(applicationId), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("offer")]
    public async Task<OutcomeResult> Offer(Guid applicationId, OfferRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new RecordOfferCommand(applicationId, request.Conditions, request.RespondByOn), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("acceptance")]
    public async Task<OutcomeResult> Acceptance(Guid applicationId, AcceptanceRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new RecordAcceptanceCommand(applicationId, request.ReceivedOn, request.PalNumber, request.Note), cancellationToken);
}
