using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Common;

/// <summary>
/// Role checks inside handlers, as a second line behind the API's authorization policies, so a handler
/// can't be reached by the wrong person even if an endpoint is misconfigured.
/// </summary>
public static class Guard
{
    public static Guid RequireAny(this ICurrentUser current, params string[] roles)
    {
        if (current.UserId is not { } id || !roles.Any(current.IsInRole))
        {
            throw new ForbiddenException();
        }

        return id;
    }

    // There are only administrators and counsellors on the staff side, and both do all of this. The separate names stay so each
    // endpoint says which kind of work it is, and so a later split (say, read-only report viewers) is a one-line change.

    /// <summary>Staff who manage the institution directory and admission applications.</summary>
    public static readonly string[] AdmissionsStaff = [Roles.Administrator, Roles.AdmissionsCounsellor];

    /// <summary>Staff who open and progress visa cases.</summary>
    public static readonly string[] VisaStaff = [Roles.Administrator, Roles.AdmissionsCounsellor];

    /// <summary>Staff who read reports (counts only, never individual records).</summary>
    public static readonly string[] ReportStaff = [Roles.Administrator, Roles.AdmissionsCounsellor];

    /// <summary>Staff who review documents and may upload on a candidate's behalf.</summary>
    public static readonly string[] DocumentStaff = [Roles.Administrator, Roles.AdmissionsCounsellor];

    /// <summary>Staff who handle individual candidate cases.</summary>
    public static readonly string[] CaseWorkers = [Roles.Administrator, Roles.AdmissionsCounsellor];
}
