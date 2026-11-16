using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Admissions;
using Nichotan.Application.Common;
using Nichotan.Application.Visas;
using Nichotan.Domain.Reminders;
using Nichotan.Domain.Tasks;
using Nichotan.Domain.Visas;

namespace Nichotan.Application.Reminders;

/// <summary>Settings for the reminder worker (the "Reminders" configuration section).</summary>
public class ReminderOptions
{
    /// <summary>Turn the worker off entirely (tests, or a second server that shouldn't send).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the worker looks for reminders that are due.</summary>
    public int RunEveryMinutes { get; set; } = 15;

    /// <summary>The business time zone the daily staff digest follows. Nichotan hasn't chosen one yet, so this is UTC until it does.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>The local time of day from which the staff digest may go out, each day.</summary>
    public string DigestTime { get; set; } = "08:00";

    /// <summary>Days after a visa application is submitted before the student is first reminded about biometrics.</summary>
    public int BiometricsFirstAfterDays { get; set; } = 3;

    /// <summary>Days between further biometrics reminders to the student.</summary>
    public int BiometricsRepeatEveryDays { get; set; } = 5;

    /// <summary>The most biometrics reminders a student gets. After that, staff follow up in person.</summary>
    public int BiometricsMaxReminders { get; set; } = 3;
}

public record ReminderRunResult(int StudentReminders, int StaffDigests);

/// <summary>
/// Sends the reminders that are due. It is safe to run as often as you like and to run twice at once: each reminder is
/// claimed in the <c>SentReminders</c> table first (unique per kind and round), so it goes out once. If sending fails the
/// claim is released, so the next run tries again.
/// </summary>
public class ReminderService(
    IAppDbContext db,
    IEmailSender email,
    IAppUrls urls,
    IOptions<AdmissionNotificationOptions> notify,
    IOptions<ReminderOptions> options,
    ILogger<ReminderService> logger)
{
    public const string BiometricsStudent = "BiometricsStudent";
    public const string StaffDigest = "StaffDigest";

    public async Task<ReminderRunResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var students = await SendBiometricsRemindersAsync(nowUtc, cancellationToken);
        var digests = await SendStaffDigestAsync(nowUtc, cancellationToken);
        return new ReminderRunResult(students, digests);
    }

    /// <summary>
    /// A student whose visa application has been submitted but whose biometrics aren't marked done gets a nudge a few days
    /// after submission, then every few days, a limited number of times. A link only; nothing about their case is in the email.
    /// </summary>
    private async Task<int> SendBiometricsRemindersAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var today = DateOnly.FromDateTime(nowUtc);
        var first = Math.Max(1, o.BiometricsFirstAfterDays);
        var every = Math.Max(1, o.BiometricsRepeatEveryDays);
        var waiting = new[] { VisaStatus.Submitted, VisaStatus.BiometricsRequested };
        var cases = await db.VisaCases.AsNoTracking().Where(v => waiting.Contains(v.Status)).Select(v => new { v.Id, v.CandidateId, v.SubmittedOn, v.StatusChangedAtUtc }).ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var c in cases)
        {
            var since = c.SubmittedOn ?? DateOnly.FromDateTime(c.StatusChangedAtUtc);
            var days = today.DayNumber - since.DayNumber;
            if (days < first) continue;
            var round = (days - first) / every;
            if (round >= o.BiometricsMaxReminders) continue;

            var profile = await db.CandidateProfiles.AsNoTracking().Where(p => p.Id == c.CandidateId).Select(p => new { p.Email, p.FirstName }).FirstOrDefaultAsync(cancellationToken);
            if (profile is null) continue;
            var message = EmailTemplates.BiometricsReminder(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/applications");
            if (await ClaimAndSendAsync(BiometricsStudent, $"{c.Id}:{round}", message, nowUtc, cancellationToken)) sent++;
        }

        return sent;
    }

    /// <summary>
    /// Once a day, from the digest time in the business time zone, staff are told if anything is overdue: biometrics not done, or a PAL
    /// that hasn't arrived, or a task past its due day. Counts and a link only, no names. If nothing is overdue nothing is sent, and it can still go out later the same day.
    /// </summary>
    private async Task<int> SendStaffDigestAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var address = notify.Value.NotifyEmail?.Trim();
        if (string.IsNullOrWhiteSpace(address)) return 0;

        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, ResolveTimeZone());
        if (!TimeOnly.TryParse(options.Value.DigestTime, out var from)) from = new TimeOnly(8, 0);
        if (TimeOnly.FromDateTime(local) < from) return 0;

        var biometrics = (await BiometricsFollowUps.ListAsync(db, DateOnly.FromDateTime(nowUtc), cancellationToken)).Count(f => f.Overdue);
        var pal = (await PalFollowUps.ListAsync(db, nowUtc, cancellationToken)).Count(f => f.Overdue);
        var today = DateOnly.FromDateTime(nowUtc);
        var tasks = await db.StaffTasks.AsNoTracking().CountAsync(t => t.Status == StaffTaskStatus.Open && t.DueOn != null && t.DueOn < today, cancellationToken);
        if (biometrics == 0 && pal == 0 && tasks == 0) return 0;

        var message = EmailTemplates.StaffDigest(address, biometrics, pal, tasks, $"{urls.WebBaseUrl}/staff");
        return await ClaimAndSendAsync(StaffDigest, DateOnly.FromDateTime(local).ToString("yyyy-MM-dd"), message, nowUtc, cancellationToken) ? 1 : 0;
    }

    /// <summary>Claims the reminder, sends it, and gives the claim back if sending fails so a later run can try again.</summary>
    private async Task<bool> ClaimAndSendAsync(string kind, string subjectKey, EmailMessage message, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (await db.SentReminders.AnyAsync(r => r.Kind == kind && r.SubjectKey == subjectKey, cancellationToken)) return false;

        var claim = new SentReminder(kind, subjectKey, nowUtc);
        db.SentReminders.Add(claim);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Someone else claimed it in the meantime: they'll send it.
            db.SentReminders.Remove(claim);
            return false;
        }

        try
        {
            await email.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "A {Kind} reminder couldn't be sent; it will be retried", kind);
            db.SentReminders.Remove(claim);
            await db.SaveChangesAsync(cancellationToken);
            return false;
        }
    }

    private TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            logger.LogWarning("Reminder time zone '{TimeZone}' isn't known; using UTC", options.Value.TimeZone);
            return TimeZoneInfo.Utc;
        }
    }
}
