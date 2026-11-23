using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Messaging;
using Nichotan.Infrastructure.Persistence;

namespace Nichotan.Infrastructure.Services;

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public class AppOptions
{
    /// <summary>The web app's public address, used in email links.</summary>
    public string WebBaseUrl { get; set; } = "http://localhost:5175";
}

public class AppUrls(IOptions<AppOptions> options) : IAppUrls
{
    public string WebBaseUrl => options.Value.WebBaseUrl.TrimEnd('/');
}

public class AuditLog(AppDbContext db, ICurrentUser current, IClock clock) : IAuditLog
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public void Add(
        AuditAction action,
        AuditOutcome outcome = AuditOutcome.Success,
        string? entityType = null,
        string? entityId = null,
        object? details = null,
        Guid? actorUserId = null,
        string? actorEmail = null)
    {
        db.AuditEvents.Add(new AuditEvent(
            clock.UtcNow,
            action,
            outcome,
            actorUserId ?? current.UserId,
            actorEmail ?? current.Email,
            entityType,
            entityId,
            current.IpAddress,
            details is null ? null : JsonSerializer.Serialize(details, Json)));
    }
}

public class SmtpOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 1028;
    public bool EnableSsl { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    /// <summary>The Automated sender (e.g. DoNotReply@nichotan.com).</summary>
    public string FromAddress { get; set; } = "no-reply@nichotan.local";
    public string FromName { get; set; } = "Nichotan";

    /// <summary>Where replies to Automated emails go (e.g. info@nichotan.com), so a reply to a no-reply address is not lost. Empty = none.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>The Info sender (e.g. info@nichotan.com), used for emails to students that invite a reply. Leave unset to send them from the Automated address.</summary>
    public SmtpSenderOverride? Info { get; set; }
}

/// <summary>
/// Settings for the Info mailbox. Only FromAddress is required; anything left empty is taken from the main Smtp section, so a provider that
/// lets one login send as both addresses needs no second password, and one that gives each mailbox its own login needs only Username and Password.
/// </summary>
public class SmtpSenderOverride
{
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public bool? EnableSsl { get; set; }
    public string? ReplyTo { get; set; }
}

/// <summary>Everything needed to send one email over SMTP, with the Automated/Info choice already made.</summary>
public record SmtpProfile(string Host, int Port, bool EnableSsl, string? Username, string? Password, string FromAddress, string FromName, string? ReplyTo)
{
    /// <summary>Picks the profile for a sender kind. Pure, so the fallback rules are easy to test.</summary>
    public static SmtpProfile For(SmtpOptions o, EmailSenderKind kind)
    {
        var automated = new SmtpProfile(o.Host, o.Port, o.EnableSsl, Blank(o.Username), Blank(o.Password), o.FromAddress, o.FromName, Blank(o.ReplyTo));
        if (kind != EmailSenderKind.Info || o.Info is not { } i || Blank(i.FromAddress) is not { } from) return automated; // not configured: use the Automated address

        // A separate login is used only when one is given. A password without its username (or the reverse) is a mistake, so it is ignored
        // rather than mixing the Info address with the other mailbox's login.
        var ownLogin = Blank(i.Username) is not null && Blank(i.Password) is not null;
        return new SmtpProfile(
            Blank(i.Host) ?? o.Host,
            i.Port ?? o.Port,
            i.EnableSsl ?? o.EnableSsl,
            ownLogin ? Blank(i.Username) : automated.Username,
            ownLogin ? Blank(i.Password) : automated.Password,
            from,
            Blank(i.FromName) ?? o.FromName,
            Blank(i.ReplyTo)); // replies to Info mail go to Info itself: no Reply-To needed
    }

    static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>
/// Wraps the real sender and records every attempt (BR-11.3). The record is written in its own scope and saved on its own, so it
/// survives whatever the caller does afterwards, and a failure to log never stops an email or hides the original error.
/// </summary>
public class LoggingEmailSender([FromKeyedServices(LoggingEmailSender.InnerKey)] IEmailSender inner, IServiceScopeFactory scopes, IClock clock, ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public const string InnerKey = "smtp";

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        try
        {
            await inner.SendAsync(message, cancellationToken);
        }
        catch (Exception ex)
        {
            await RecordAsync(message, MessageState.Failed, ex.Message);
            throw;
        }
        await RecordAsync(message, MessageState.Sent, null);
    }

    private async Task RecordAsync(EmailMessage message, MessageState state, string? error)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.MessageLogs.Add(new MessageLog(MessageChannel.Email, message.Subject, MessageLog.MaskRecipient(message.To), state, error, clock.UtcNow, message.From.ToString()));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record a message attempt.");
        }
    }
}

/// <summary>Sends through any SMTP server (Mailpit locally; the domain's mail host or a transactional provider in production).</summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var p = SmtpProfile.For(options.Value, message.From);
        using var client = new SmtpClient(p.Host, p.Port) { EnableSsl = p.EnableSsl };
        if (!string.IsNullOrEmpty(p.Username)) client.Credentials = new NetworkCredential(p.Username, p.Password);

        using var mail = new MailMessage { From = new MailAddress(p.FromAddress, p.FromName), Subject = message.Subject };
        mail.To.Add(message.To);
        if (!string.IsNullOrEmpty(p.ReplyTo)) mail.ReplyToList.Add(new MailAddress(p.ReplyTo));
        // Both parts as alternate views, so clients pick HTML and the HTML part isn't mislabelled as plain text.
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
        await client.SendMailAsync(mail, cancellationToken);
    }
}
