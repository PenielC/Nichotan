using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Nichotan.Api.Security;
using Nichotan.Application.Candidates;
using Nichotan.Application.Enquiries;
using Nichotan.Domain.Enquiries;

namespace Nichotan.Api.Controllers;

/// <summary>The public enquiry form. <c>Website</c> is a hidden trap field: people leave it empty, simple bots fill it in.</summary>
public record SubmitEnquiryRequest(
    string Name,
    string? Email,
    string? Phone,
    Qualification HighestQualification,
    int GraduationYear,
    string? PreferredProgramme,
    bool OpenToGuidance,
    DestinationInterest DestinationInterest,
    string? Message,
    string? Website);

public record UpdateEnquiryRequest(EnquiryStatus Status, string? StaffNote);

[ApiController]
[Route("api/enquiries")]
public class EnquiriesController(ISender sender) : ControllerBase
{
    /// <summary>Anyone can send an enquiry, with no account. Rate limited by address.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimits.Email)]
    [HttpPost]
    public async Task<IActionResult> Submit(SubmitEnquiryRequest request, CancellationToken cancellationToken)
    {
        // A bot that filled the hidden field gets the same answer as a person, and nothing is saved.
        if (!string.IsNullOrWhiteSpace(request.Website)) return NoContent();
        await sender.Send(
            new SubmitEnquiryCommand(request.Name, request.Email, request.Phone, request.HighestQualification, request.GraduationYear, request.PreferredProgramme, request.OpenToGuidance, request.DestinationInterest, request.Message),
            cancellationToken);
        return NoContent();
    }

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet]
    public async Task<PagedResult<EnquiryResult>> List([FromQuery] EnquiryStatus? status, [FromQuery] string? search, [FromQuery] bool unviewedOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        await sender.Send(new ListEnquiriesQuery(status, search, unviewedOnly, page, pageSize), cancellationToken);

    [Authorize(Policy = Policies.CaseWorker)]
    [HttpGet("summary")]
    public async Task<EnquirySummary> Summary(CancellationToken cancellationToken) => await sender.Send(new GetEnquirySummaryQuery(), cancellationToken);

    /// <summary>Marks an enquiry as opened for review, by an administrator or counsellor.</summary>
    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPost("{id:guid}/viewed")]
    public async Task<EnquiryResult> Viewed(Guid id, CancellationToken cancellationToken) => await sender.Send(new MarkEnquiryViewedCommand(id), cancellationToken);

    [Authorize(Policy = Policies.AdmissionsStaff)]
    [HttpPut("{id:guid}")]
    public async Task<EnquiryResult> Update(Guid id, UpdateEnquiryRequest request, CancellationToken cancellationToken) =>
        await sender.Send(new UpdateEnquiryCommand(id, request.Status, request.StaffNote), cancellationToken);
}
