using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nichotan.Api.Security;
using Nichotan.Application.Reporting;
using Nichotan.Domain.Applications;

namespace Nichotan.Api.Controllers;

/// <summary>Reports. Aggregate counts only: no individual candidate appears in them.</summary>
[ApiController]
[Route("api/reports")]
public class ReportsController(ISender sender) : ControllerBase
{
    /// <summary>One month: where records stand (current pipeline), what happened in the month (movement), and cohort conversions.</summary>
    [Authorize(Policy = Policies.Reports)]
    [HttpGet("monthly")]
    public async Task<MonthlyReportResult> Monthly(
        [FromQuery] string month,
        [FromQuery] DestinationCountry? country,
        [FromQuery] ApplicantLevel? level,
        [FromQuery] Guid? institutionId,
        [FromQuery] StartTerm? intakeTerm,
        [FromQuery] int? intakeYear,
        CancellationToken cancellationToken) =>
        await sender.Send(new GetMonthlyReportQuery(month, country, level, institutionId, intakeTerm, intakeYear), cancellationToken);
}
