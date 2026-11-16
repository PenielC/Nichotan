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
    public string FromAddress { get; set; } = "no-reply@nichotan.local";
    public string FromName { get; set; } = "Nichotan";
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
            db.MessageLogs.Add(new MessageLog(MessageChannel.Email, message.Subject, MessageLog.MaskRecipient(message.To), state, error, clock.UtcNow));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not record a message attempt.");
        }
    }
}

/// <summary>Sends through any SMTP server (Mailpit locally; a transactional email provider in production).</summary>
public class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        using var client = new SmtpClient(o.Host, o.Port) { EnableSsl = o.EnableSsl };
        if (!string.IsNullOrEmpty(o.Username)) client.Credentials = new NetworkCredential(o.Username, o.Password);

        using var mail = new MailMessage { From = new MailAddress(o.FromAddress, o.FromName), Subject = message.Subject };
        mail.To.Add(message.To);
        // Both parts as alternate views, so clients pick HTML and the HTML part isn't mislabelled as plain text.
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.TextBody, null, "text/plain"));
        mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(message.HtmlBody, null, "text/html"));
        await client.SendMailAsync(mail, cancellationToken);
    }
}
