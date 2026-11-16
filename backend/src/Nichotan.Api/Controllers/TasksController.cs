using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Candidates;
using Nichotan.Application.Tasks;
using Nichotan.Domain.Tasks;

namespace Nichotan.Api.Controllers;

public record CreateTaskRequest(string Title, string? Notes, TaskPriority Priority = TaskPriority.Normal, DateOnly? DueOn = null, Guid? AssigneeUserId = null, Guid? CandidateId = null, Guid? ApplicationId = null, Guid? VisaCaseId = null);
public record UpdateTaskRequest(string Title, string? Notes, TaskPriority Priority, DateOnly? DueOn, Guid AssigneeUserId);
public record TaskStatusRequest(string Action);
public record WhatsAppShareRequest(string Topic);

/// <summary>Staff tasks, and a student's timeline. Staff only: students never see either.</summary>
[ApiController]
public class TasksController(ISender sender) : ControllerBase
{
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/tasks")]
    public async Task<PagedResult<TaskResult>> List(
        [FromQuery] bool mine, [FromQuery] Guid? assigneeUserId, [FromQuery] StaffTaskStatus? status, [FromQuery] Guid? candidateId, [FromQuery] Guid? applicationId, [FromQuery] Guid? visaCaseId,
        [FromQuery] bool overdueOnly, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListTasksQuery(mine, assigneeUserId, status, candidateId, applicationId, visaCaseId, overdueOnly, page, pageSize), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/tasks/summary")]
    public async Task<TaskSummary> Summary(CancellationToken cancellationToken) => await sender.Send(new GetTaskSummaryQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/tasks/assignees")]
    public async Task<IReadOnlyList<Assignee>> Assignees(CancellationToken cancellationToken) => await sender.Send(new ListAssigneesQuery(), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpPost("api/tasks")]
    public async Task<TaskResult> Create(CreateTaskRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new CreateTaskCommand(request.Title, request.Notes, request.Priority, request.DueOn, request.AssigneeUserId, request.CandidateId, request.ApplicationId, request.VisaCaseId), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpPut("api/tasks/{id:guid}")]
    public async Task<TaskResult> Update(Guid id, UpdateTaskRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateTaskCommand(id, request.Title, request.Notes, request.Priority, request.DueOn, request.AssigneeUserId), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpPost("api/tasks/{id:guid}/status")]
    public async Task<TaskResult> ChangeStatus(Guid id, TaskStatusRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new ChangeTaskStatusCommand(id, request.Action), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("api/candidates/{id:guid}/timeline")]
    public async Task<IReadOnlyList<TimelineEntry>> Timeline(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetCandidateTimelineQuery(id), cancellationToken);

    /// <summary>Records that staff opened WhatsApp with one of a student's lists ready to send (the browser opens WhatsApp itself).</summary>
    [Authorize(Policy = Policies.CaseWorker)]
    [HttpPost("api/candidates/{id:guid}/whatsapp-shares")]
    public async Task<IActionResult> WhatsAppShare(Guid id, WhatsAppShareRequest request, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordWhatsAppShareCommand(id, request.Topic), cancellationToken);
        return NoContent();
    }
}
