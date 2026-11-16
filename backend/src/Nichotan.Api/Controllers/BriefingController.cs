using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Briefing;

namespace Nichotan.Api.Controllers;

/// <summary>The signed-in staff member's "Today" list, worked out from fixed rules.</summary>
[ApiController]
[Route("api/briefing")]
public class BriefingController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<BriefingResult> Get(CancellationToken cancellationToken) => await sender.Send(new GetBriefingQuery(), cancellationToken);
}
