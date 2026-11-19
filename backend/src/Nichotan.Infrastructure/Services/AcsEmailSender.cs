using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using AcsEmailMessage = Azure.Communication.Email.EmailMessage;
using EmailMessage = Nichotan.Application.Common.EmailMessage;

namespace Nichotan.Infrastructure.Services;

/// <summary>Settings for Azure Communication Services Email (the "Email:Acs" configuration section).</summary>
public class AcsEmailOptions
{
    /// <summary>The Communication Services resource's connection string (a secret).</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>A sender address on a verified domain, e.g. DoNotReply@yourdomain.com or the Azure-managed one.</summary>
    public string FromAddress { get; set; } = string.Empty;
}

/// <summary>Sends through Azure Communication Services Email. Throws on failure so the logging decorator records it.</summary>
public class AcsEmailSender(IOptions<AcsEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var client = new EmailClient(o.ConnectionString);
        var content = new EmailContent(message.Subject) { PlainText = message.TextBody, Html = message.HtmlBody };
        var email = new AcsEmailMessage(o.FromAddress, message.To, content);
        // WaitUntil.Started: accepted for delivery is enough; we don't hold the request while the provider delivers.
        await client.SendAsync(WaitUntil.Started, email, cancellationToken);
    }
}
