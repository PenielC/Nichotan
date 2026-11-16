using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Admissions;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Common;
using Nichotan.Domain.Reporting;
using Nichotan.Domain.Visas;

namespace Nichotan.Application.Reporting;

// The monthly report. Current pipeline (where records stand) and monthly movement (what happened in the month) are separate
// blocks with their own labels, both rebuilt from the recorded transitions, so any past month can be reported.

public record StatusCount(string Status, int Count);

/// <summary>
/// A cohort conversion: of the records that were submitted during the month, how many ended up where, as at the end of the month.
/// The denominator is always the cohort, never "everything open", so the rate can't be inflated by records from other months.
/// </summary>
public record ConversionResult(string Name, string Definition, int Cohort, int Succeeded, int Declined, int Pending);

/// <summary>Counts that aren't about a status. Registrations and enquiries ignore the filters (they have no country or school yet).</summary>
public record ActivityCounts(int Registrations, int Enquiries, int AcceptancesRecorded);

public record MonthlyReportResult(
    string Month,
    DateOnly From,
    DateOnly To,
    DateOnly AsAt,
    IReadOnlyList<StatusCount> AdmissionPipeline,
    IReadOnlyList<StatusCount> AdmissionMovement,
    IReadOnlyList<StatusCount> VisaPipeline,
    IReadOnlyList<StatusCount> VisaMovement,
    IReadOnlyList<ConversionResult> Conversions,
    ActivityCounts Activity);

public record GetMonthlyReportQuery(string Month, DestinationCountry? Country, ApplicantLevel? Level, Guid? InstitutionId, StartTerm? IntakeTerm, int? IntakeYear) : IQuery<MonthlyReportResult>;

public class GetMonthlyReportValidator : AbstractValidator<GetMonthlyReportQuery>
{
    public GetMonthlyReportValidator()
    {
        RuleFor(x => x.Month).Must(m => DateOnly.TryParseExact(m + "-01", "yyyy-MM-dd", out _)).WithMessage("Choose a month, such as 2026-10.");
        RuleFor(x => x.IntakeYear).InclusiveBetween(2000, 2100).When(x => x.IntakeYear.HasValue);
    }
}

public class GetMonthlyReportHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<GetMonthlyReportQuery, MonthlyReportResult>
{
    public async ValueTask<MonthlyReportResult> Handle(GetMonthlyReportQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.ReportStaff);
        var from = DateOnly.ParseExact(query.Month + "-01", "yyyy-MM-dd");
        var to = from.AddMonths(1).AddDays(-1);
        var today = DateOnly.FromDateTime(clock.UtcNow);
        if (from > today) throw new DomainException("Choose this month or an earlier one.");
        // For the month still running, "as at" is today: nothing later has happened yet.
        var asAt = to < today ? to : today;

        // Applications that match the filters; everything else is measured within this set.
        var applications = db.AdmissionApplications.AsNoTracking().AsQueryable();
        if (query.Country is { } country) applications = applications.Where(a => a.Country == country);
        if (query.Level is { } level) applications = applications.Where(a => a.Level == level);
        if (query.InstitutionId is { } institutionId) applications = applications.Where(a => a.InstitutionId == institutionId);
        if (query.IntakeTerm is { } term) applications = applications.Where(a => a.IntakeTerm == term);
        if (query.IntakeYear is { } year) applications = applications.Where(a => a.IntakeYear == year);
        var applicationIds = await applications.Select(a => a.Id).ToListAsync(cancellationToken);

        var admissionTransitions = (await db.AdmissionStatusEvents.AsNoTracking().Where(e => applicationIds.Contains(e.ApplicationId)).Select(e => new { e.ApplicationId, e.To, e.OccurredOn, e.RecordedAtUtc, e.IsCorrection }).ToListAsync(cancellationToken))
            .Select(e => new Transition(e.ApplicationId, e.To.ToString(), e.OccurredOn, e.RecordedAtUtc, e.IsCorrection)).ToList();

        var visas = await db.VisaCases.AsNoTracking().Where(v => applicationIds.Contains(v.ApplicationId)).Select(v => v.Id).ToListAsync(cancellationToken);
        var visaTransitions = (await db.VisaStatusEvents.AsNoTracking().Where(e => visas.Contains(e.VisaCaseId)).Select(e => new { e.VisaCaseId, e.To, e.OccurredOn, e.RecordedAtUtc, e.IsCorrection }).ToListAsync(cancellationToken))
            .Select(e => new Transition(e.VisaCaseId, e.To.ToString(), e.OccurredOn, e.RecordedAtUtc, e.IsCorrection)).ToList();

        var acceptances = await db.AdmissionAcceptances.AsNoTracking().Where(a => applicationIds.Contains(a.ApplicationId) && a.ReceivedOn >= from && a.ReceivedOn <= to).CountAsync(cancellationToken);
        var start = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var registrations = await db.CandidateProfiles.AsNoTracking().CountAsync(p => p.CreatedAtUtc >= start && p.CreatedAtUtc < end, cancellationToken);
        var enquiries = await db.Enquiries.AsNoTracking().CountAsync(e => e.CreatedAtUtc >= start && e.CreatedAtUtc < end, cancellationToken);

        return new MonthlyReportResult(
            query.Month, from, to, asAt,
            Pipeline<AdmissionStatus>(admissionTransitions, asAt),
            Movement<AdmissionStatus>(admissionTransitions, from, to),
            Pipeline<VisaStatus>(visaTransitions, asAt),
            Movement<VisaStatus>(visaTransitions, from, to),
            [
                Conversion(
                    "Admission applications submitted to a school",
                    "Applications recorded as submitted to the school during the month. Of those, as at the end of the month: received an offer (ever), were rejected (currently), or are still waiting.",
                    admissionTransitions, nameof(AdmissionStatus.SubmittedToInstitution), nameof(AdmissionStatus.OfferReceived), nameof(AdmissionStatus.Rejected), from, to, asAt),
                Conversion(
                    "Visa applications submitted",
                    "Visa cases recorded as submitted during the month. Of those, as at the end of the month: approved (ever), refused (currently), or still waiting.",
                    visaTransitions, nameof(VisaStatus.Submitted), nameof(VisaStatus.Approved), nameof(VisaStatus.Refused), from, to, asAt),
            ],
            new ActivityCounts(registrations, enquiries, acceptances));
    }

    /// <summary>How many records were in each status as at the day. Every status is listed, zeros included, in workflow order.</summary>
    private static List<StatusCount> Pipeline<TStatus>(IReadOnlyList<Transition> transitions, DateOnly asAt) where TStatus : struct, Enum
    {
        var now = ReportMath.StatusAsAt(transitions, asAt).Values.GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        return Enum.GetNames<TStatus>().Select(name => new StatusCount(name, now.GetValueOrDefault(name))).ToList();
    }

    /// <summary>How many records moved into each status during the period, each counted once per status.</summary>
    private static List<StatusCount> Movement<TStatus>(IReadOnlyList<Transition> transitions, DateOnly from, DateOnly to) where TStatus : struct, Enum =>
        Enum.GetNames<TStatus>().Select(name => new StatusCount(name, ReportMath.MovedInto(transitions, name, from, to).Count)).ToList();

    private static ConversionResult Conversion(string name, string definition, IReadOnlyList<Transition> transitions, string submitted, string success, string declined, DateOnly from, DateOnly to, DateOnly asAt)
    {
        var cohort = ReportMath.MovedInto(transitions, submitted, from, to);
        var reached = ReportMath.EverReached(transitions, success, asAt);
        var status = ReportMath.StatusAsAt(transitions, asAt);
        var succeeded = cohort.Count(id => reached.Contains(id));
        var turnedDown = cohort.Count(id => !reached.Contains(id) && status.GetValueOrDefault(id) == declined);
        return new ConversionResult(name, definition, cohort.Count, succeeded, turnedDown, cohort.Count - succeeded - turnedDown);
    }
}
