using Nichotan.Domain.Common;

namespace Nichotan.Domain.Messaging;

public enum MessageChannel
{
    Email,
    WhatsApp,
}

public enum MessageState
{
    Sent,
    Failed,
}

/// <summary>
/// One attempt to send a message (BR-11.3). It keeps what was sent and how it went, never the body: the recipient is masked and the
/// error is trimmed, so the log can be read by an administrator without exposing anything a message might have carried.
/// </summary>
public class MessageLog : Entity
{
    private MessageLog() { }

    public MessageLog(MessageChannel channel, string subject, string recipient, MessageState state, string? error, DateTime attemptedAtUtc, string sender = "Automated")
    {
        Sender = Trim(sender, 20);
        Channel = channel;
        Subject = Trim(subject, 200);
        Recipient = recipient;
        State = state;
        Error = error is null ? null : Trim(error, 300);
        AttemptedAtUtc = attemptedAtUtc;
    }

    public MessageChannel Channel { get; private set; }

    /// <summary>The email subject (or template name), which says what kind of message it was.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>The recipient as <see cref="MaskRecipient"/> writes it, such as "j***@example.com".</summary>
    public string Recipient { get; private set; } = string.Empty;

    public MessageState State { get; private set; }

    /// <summary>Why it failed. A short reason only.</summary>
    public string? Error { get; private set; }

    public DateTime AttemptedAtUtc { get; private set; }

    /// <summary>Which of the organisation's addresses sent it: "Automated" (system mail) or "Info" (a reply-welcome mailbox). Not the address itself, which is configuration.</summary>
    public string Sender { get; private set; } = "Automated";

    /// <summary>"jane.doe@example.com" becomes "j***@example.com"; a phone number keeps its country code and last two digits.</summary>
    public static string MaskRecipient(string recipient)
    {
        var value = recipient.Trim();
        var at = value.IndexOf('@');
        if (at > 0) return value[0] + "***" + value[at..];
        if (value.Length > 6) return value[..4] + "***" + value[^2..];
        return "***";
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max];
}
