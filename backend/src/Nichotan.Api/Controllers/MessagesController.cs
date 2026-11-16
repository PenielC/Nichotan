using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Messaging;
using Nichotan.Domain.Messaging;

namespace Nichotan.Api.Controllers;

/// <summary>The record of messages the system has tried to send. Recipients are masked and no message body is kept.</summary>
[ApiController]
[Route("api/messages")]
public class MessagesController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.Administrator)]
    [HttpGet]
    public async Task<MessageLogResult> List([FromQuery] MessageState? state, CancellationToken cancellationToken) =>
        await sender.Send(new ListMessageLogQuery(state), cancellationToken);
}
