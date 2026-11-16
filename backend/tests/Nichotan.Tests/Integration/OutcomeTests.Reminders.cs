using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Application.Reminders;

namespace Nichotan.Tests.Integration;

// The reminder service is driven directly with a time the test chooses: nothing waits for a real clock.
public partial class OutcomeTests
{
    private async Task<ReminderRunResult> RunRemindersAsync(DateTime nowUtc)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(nowUtc, CancellationToken.None);
    }

    private static int Received(FakeEmailSender emails, string to, string subject) => emails.Sent.Count(m => m.To == to && m.Subject == subject);

    [Fact]
    public async Task A_student_is_nudged_about_biometrics_a_few_days_after_submission_then_every_few_days_a_limited_number_of_times_and_never_twice()
    {
        var (_, counsellor, student, _, _, visaId) = await WithVisaCaseAsync();
        var email = (await (await student.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Email;
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        var submitted = DateTime.UtcNow;
        const string subject = "A reminder about your biometrics";

        // Too soon, no reminder.
        await RunRemindersAsync(submitted.AddDays(2));
        Received(api.Emails, email, subject).Should().Be(0);

        // Three days in: the first one. Running again, however often, sends nothing more.
        await RunRemindersAsync(submitted.AddDays(3));
        await RunRemindersAsync(submitted.AddDays(3));
        await RunRemindersAsync(submitted.AddDays(4));
        Received(api.Emails, email, subject).Should().Be(1);
        api.Emails.Sent.Single(m => m.To == email && m.Subject == subject).TextBody.Should().Contain("/portal/applications").And.NotContain(visaId.ToString(), "a link only, nothing about the case");

        // Then every five days, up to three in all.
        await RunRemindersAsync(submitted.AddDays(8));
        Received(api.Emails, email, subject).Should().Be(2);
        await RunRemindersAsync(submitted.AddDays(13));
        await RunRemindersAsync(submitted.AddDays(13));
        Received(api.Emails, email, subject).Should().Be(3);
        await RunRemindersAsync(submitted.AddDays(18));
        await RunRemindersAsync(submitted.AddDays(40));
        Received(api.Emails, email, subject).Should().Be(3, "after three, staff follow up in person");
    }

    [Fact]
    public async Task Once_biometrics_are_done_the_reminders_stop()
    {
        var (_, counsellor, student, _, _, visaId) = await WithVisaCaseAsync();
        var email = (await (await student.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Email;
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        await MoveVisaAsync(counsellor, visaId, "BiometricsCompleted");

        await RunRemindersAsync(DateTime.UtcNow.AddDays(4));
        Received(api.Emails, email, "A reminder about your biometrics").Should().Be(0);
    }

    [Fact]
    public async Task Staff_get_one_digest_a_day_from_the_digest_time_and_only_when_something_is_overdue()
    {
        var (_, counsellor, _, _, _, visaId) = await WithVisaCaseAsync();
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        const string subject = "Follow-ups are overdue";
        const string staffMailbox = "enquiries@nichotan.com";
        var day = DateTime.UtcNow.Date.AddDays(10); // far enough on that this case, and others, are overdue
        var before = Received(api.Emails, staffMailbox, subject);

        // Before the digest time (08:00 in the business time zone, UTC for now): nothing.
        await RunRemindersAsync(day.AddHours(7));
        Received(api.Emails, staffMailbox, subject).Should().Be(before);

        // From the digest time: one email, however many times the worker runs that day.
        await RunRemindersAsync(day.AddHours(9));
        await RunRemindersAsync(day.AddHours(15));
        Received(api.Emails, staffMailbox, subject).Should().Be(before + 1);
        var digest = api.Emails.Sent.Last(m => m.To == staffMailbox && m.Subject == subject);
        digest.TextBody.Should().Contain("overdue").And.Contain("/staff").And.NotContain("@example", "counts and a link only, no names");

        // The next day it goes again, because things are still overdue.
        await RunRemindersAsync(day.AddDays(1).AddHours(9));
        Received(api.Emails, staffMailbox, subject).Should().Be(before + 2);
    }

    [Fact]
    public async Task A_reminder_that_fails_to_send_is_tried_again_and_not_lost_or_doubled()
    {
        var (_, counsellor, student, _, _, visaId) = await WithVisaCaseAsync();
        var email = (await (await student.GetAsync("/api/candidates/me")).Read<ProfileDto>()).Email;
        await MoveVisaAsync(counsellor, visaId, "Submitted");
        var now = DateTime.UtcNow.AddDays(3);

        // A mail server that is down: nothing is recorded as sent.
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var down = new ReminderService(scope.ServiceProvider.GetRequiredService<IAppDbContext>(), new ThrowingEmailSender(), scope.ServiceProvider.GetRequiredService<IAppUrls>(),
                scope.ServiceProvider.GetRequiredService<IOptions<AdmissionNotificationOptions>>(), scope.ServiceProvider.GetRequiredService<IOptions<ReminderOptions>>(), NullLogger<ReminderService>.Instance);
            await down.RunAsync(now, CancellationToken.None);
        }

        Received(api.Emails, email, "A reminder about your biometrics").Should().Be(0);

        // When the mail server is back the next run sends it, once.
        await RunRemindersAsync(now);
        await RunRemindersAsync(now);
        Received(api.Emails, email, "A reminder about your biometrics").Should().Be(1);
    }

    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("The mail server is down.");
    }
}
