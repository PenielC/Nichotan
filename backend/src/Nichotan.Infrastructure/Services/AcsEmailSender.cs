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

    /// <summary>The Automated sender: an address on a verified domain, e.g. DoNotReply@yourdomain.com or the Azure-managed one.</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>
    /// The Info sender, for emails to students that invite a reply (e.g. info@yourdomain.com). It must be a MailFrom address on the same verified domain
    /// (Email Communication Service, Domains, MailFrom addresses). Leave empty to send those emails from the Automated address.
    /// </summary>
    public string? InfoFromAddress { get; set; }

    /// <summary>Where replies to Automated emails go (e.g. info@yourdomain.com). Empty = none.</summary>
    public string? ReplyTo { get; set; }

    /// <summary>Which address to send from, with the fallback rule in one testable place.</summary>
    public string AddressFor(EmailSenderKind kind) =>
        kind == EmailSenderKind.Info && !string.IsNullOrWhiteSpace(InfoFromAddress) ? InfoFromAddress.Trim() : FromAddress;

    /// <summary>Automated mail directs replies to ReplyTo; Info mail needs none, because replies already reach that mailbox.</summary>
    public string? ReplyToFor(EmailSenderKind kind) =>
        kind == EmailSenderKind.Info && !string.IsNullOrWhiteSpace(InfoFromAddress) ? null : (string.IsNullOrWhiteSpace(ReplyTo) ? null : ReplyTo.Trim());
}

/// <summary>Sends through Azure Communication Services Email. Throws on failure so the logging decorator records it.</summary>
public class AcsEmailSender(IOptions<AcsEmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        var client = new EmailClient(o.ConnectionString);
        var content = new EmailContent(message.Subject) { PlainText = message.TextBody, Html = message.HtmlBody };
        var email = new AcsEmailMessage(o.AddressFor(message.From), message.To, content);
        if (o.ReplyToFor(message.From) is { } replyTo) email.ReplyTo.Add(new EmailAddress(replyTo));
        // WaitUntil.Started: accepted for delivery is enough; we don't hold the request while the provider delivers.
        await client.SendAsync(WaitUntil.Started, email, cancellationToken);
    }
}
