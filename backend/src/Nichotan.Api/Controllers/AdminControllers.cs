using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Audit;
using Nichotan.Application.Candidates;
using Nichotan.Application.Staff;
using Nichotan.Domain.Audit;

namespace Nichotan.Api.Controllers;

public record InviteStaffRequest(string Email, string FirstName, string LastName, IReadOnlyList<string> Roles);
public record RolesRequest(IReadOnlyList<string> Roles);
public record UpdateProfileRequest(string FirstName, string LastName, string? Phone);

/// <summary>Staff accounts. Administrators only.</summary>
[ApiController]
[Route("api/staff")]
[Authorize(Policy = Policies.Administrator)]
public class StaffController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<StaffMember>> List(CancellationToken cancellationToken) => await sender.Send(new ListStaffQuery(), cancellationToken);

    [HttpPost]
    public async Task<StaffMember> Invite(InviteStaffRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new InviteStaffCommand(request.Email, request.FirstName, request.LastName, request.Roles), cancellationToken);

    [HttpPost("{id:guid}/resend-invitation")]
    public async Task<IActionResult> ResendInvitation(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new ResendInvitationCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<StaffMember> SetRoles(Guid id, RolesRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateStaffRolesCommand(id, request.Roles), cancellationToken);

    [HttpPost("{id:guid}/disable")]
    public async Task<StaffMember> Disable(Guid id, CancellationToken cancellationToken) => await sender.Send(new SetStaffDisabledCommand(id, true), cancellationToken);

    [HttpPost("{id:guid}/enable")]
    public async Task<StaffMember> Enable(Guid id, CancellationToken cancellationToken) => await sender.Send(new SetStaffDisabledCommand(id, false), cancellationToken);

    [HttpPost("{id:guid}/reset-mfa")]
    public async Task<StaffMember> ResetMfa(Guid id, CancellationToken cancellationToken) => await sender.Send(new ResetStaffMfaCommand(id), cancellationToken);
}

[ApiController]
[Route("api/candidates")]
public class CandidatesController(ISender sender) : ControllerBase
{
    /// <summary>The signed-in candidate's own profile. There is no candidate-facing endpoint that takes another candidate's id.</summary>
    [Authorize(Policy = Policies.Candidate)]
    [HttpGet("me")]
    public async Task<CandidateProfileResult> MyProfile(CancellationToken cancellationToken) => await sender.Send(new GetMyCandidateProfileQuery(), cancellationToken);

    [Authorize(Policy = Policies.Candidate)]
    [HttpPut("me")]
    public async Task<CandidateProfileResult> UpdateMyProfile(UpdateProfileRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateMyCandidateProfileCommand(request.FirstName, request.LastName, request.Phone), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<PagedResult<CandidateSummary>> List([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListCandidatesQuery(search, page, pageSize), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("{id:guid}")]
    public async Task<CandidateProfileResult> Get(Guid id, CancellationToken cancellationToken) => await sender.Send(new GetCandidateQuery(id), cancellationToken);
}

/// <summary>The audit trail. Administrators only.</summary>
[ApiController]
[Route("api/audit-events")]
[Authorize(Policy = Policies.Administrator)]
public class AuditController(ISender sender) : ControllerBase
{
    [HttpGet]
    public async Task<PagedResult<AuditEventResult>> List(
        [FromQuery] AuditAction? action,
        [FromQuery] AuditOutcome? outcome,
        [FromQuery] Guid? actorUserId,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        await sender.Send(new ListAuditEventsQuery(action, outcome, actorUserId, entityType, entityId, fromUtc, toUtc, page, pageSize), cancellationToken);

    /// <summary>The action names, for filters.</summary>
    [HttpGet("actions")]
    public IReadOnlyList<string> Actions() => Enum.GetNames<AuditAction>();
}
