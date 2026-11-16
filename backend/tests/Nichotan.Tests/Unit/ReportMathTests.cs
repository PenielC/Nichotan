using AwesomeAssertions;
using Nichotan.Domain.Reporting;

namespace Nichotan.Tests.Unit;

public class ReportMathTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static Transition T(Guid id, string status, string day, int recordedOrder = 0, bool correction = false) =>
        new(id, status, DateOnly.Parse(day), new DateTime(2026, 11, 1, 9, 0, recordedOrder, DateTimeKind.Utc), correction);

    [Fact]
    public void The_pipeline_is_where_each_record_stood_at_the_end_of_the_day_not_where_it_is_now()
    {
        var moves = new[] { T(A, "Draft", "2026-09-10", 1), T(A, "Submitted", "2026-09-30", 2), T(A, "Offer", "2026-10-01", 3), T(B, "Draft", "2026-10-15", 4) };

        var endOfSeptember = ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-09-30"));
        endOfSeptember[A].Should().Be("Submitted", "a move on the last day of the month counts for that month");
        endOfSeptember.Should().NotContainKey(B, "it didn't exist yet");
        ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-10-31")).Should().Contain(new KeyValuePair<Guid, string>(A, "Offer")).And.Contain(new KeyValuePair<Guid, string>(B, "Draft"));
    }

    [Fact]
    public void A_move_entered_later_but_dated_earlier_is_still_the_latest_status_and_belongs_to_its_own_day()
    {
        // Added to the system on 10 Nov, then staff enter that it had already been ready for review on 15 Oct.
        var moves = new[] { T(A, "Draft", "2026-11-10", 1), T(A, "ReadyForReview", "2026-10-15", 2) };

        ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-10-31"))[A].Should().Be("ReadyForReview", "that is all that had happened by the end of October");
        ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-11-30"))[A].Should().Be("ReadyForReview", "it was recorded after the Draft, so it is where the record stands");
    }

    [Fact]
    public void Two_moves_on_one_day_resolve_to_the_one_recorded_last_and_corrections_say_what_the_status_really_was()
    {
        var moves = new[] { T(A, "Submitted", "2026-10-05", recordedOrder: 1), T(A, "Offer", "2026-10-05", recordedOrder: 2), T(A, "Rejected", "2026-10-06", recordedOrder: 3, correction: true) };

        ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-10-05"))[A].Should().Be("Offer");
        ReportMath.StatusAsAt(moves, DateOnly.Parse("2026-10-31"))[A].Should().Be("Rejected", "the correction is the truth from the day it applies");
    }

    [Fact]
    public void Movement_counts_each_record_once_inside_the_month_including_both_ends_and_ignores_corrections()
    {
        var moves = new[]
        {
            T(A, "Offer", "2026-10-01"), T(A, "Offer", "2026-10-20"), // moved there twice: counts once
            T(B, "Offer", "2026-09-30"),                               // the day before the month
            T(Guid.NewGuid(), "Offer", "2026-10-31"),                   // last day of the month
            T(Guid.NewGuid(), "Offer", "2026-11-01"),                   // the day after
            T(Guid.NewGuid(), "Offer", "2026-10-10", correction: true), // a correction isn't something that happened
        };

        ReportMath.MovedInto(moves, "Offer", DateOnly.Parse("2026-10-01"), DateOnly.Parse("2026-10-31")).Should().HaveCount(2);
    }

    [Fact]
    public void Ever_reached_looks_only_up_to_the_reporting_day()
    {
        var moves = new[] { T(A, "Offer", "2026-10-20"), T(B, "Offer", "2026-11-03") };

        ReportMath.EverReached(moves, "Offer", DateOnly.Parse("2026-10-31")).Should().BeEquivalentTo([A]);
    }
}
