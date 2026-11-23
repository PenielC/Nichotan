using AwesomeAssertions;
using Nichotan.Application.Common;
using Nichotan.Infrastructure.Services;

namespace Nichotan.Tests;

/// <summary>Which of the two addresses an email leaves from, and what happens when the second one is not set up.</summary>
public class SmtpProfileTests
{
    private static SmtpOptions Options(Action<SmtpOptions>? tweak = null)
    {
        var o = new SmtpOptions
        {
            Host = "mail.nichotan.com", Port = 587, EnableSsl = true,
            Username = "DoNotReply@nichotan.com", Password = "pw-auto",
            FromAddress = "DoNotReply@nichotan.com", FromName = "Nichotan",
            ReplyTo = "info@nichotan.com",
        };
        tweak?.Invoke(o);
        return o;
    }

    [Fact]
    public void Automated_mail_comes_from_the_automated_address_and_replies_go_to_info()
    {
        var p = SmtpProfile.For(Options(), EmailSenderKind.Automated);
        p.FromAddress.Should().Be("DoNotReply@nichotan.com");
        p.ReplyTo.Should().Be("info@nichotan.com", "a reply to a no-reply address must not be lost");
        p.Username.Should().Be("DoNotReply@nichotan.com");
    }

    [Fact]
    public void Info_mail_falls_back_to_the_automated_address_until_the_info_address_is_configured()
    {
        // Nothing breaks if the second mailbox is not set up yet.
        var none = SmtpProfile.For(Options(), EmailSenderKind.Info);
        none.Should().Be(SmtpProfile.For(Options(), EmailSenderKind.Automated));

        var blank = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "   " }), EmailSenderKind.Info);
        blank.FromAddress.Should().Be("DoNotReply@nichotan.com");
    }

    [Fact]
    public void Info_mail_uses_the_info_address_and_the_shared_login_when_it_has_no_login_of_its_own()
    {
        var p = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "info@nichotan.com" }), EmailSenderKind.Info);
        p.FromAddress.Should().Be("info@nichotan.com");
        p.Username.Should().Be("DoNotReply@nichotan.com", "a provider that lets one login send as both needs no second password");
        p.Password.Should().Be("pw-auto");
        p.Host.Should().Be("mail.nichotan.com");
        p.Port.Should().Be(587);
        p.FromName.Should().Be("Nichotan");
        p.ReplyTo.Should().BeNull("replies to Info mail already reach the Info mailbox");
    }

    [Fact]
    public void Info_mail_uses_its_own_login_when_it_has_one()
    {
        var p = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "info@nichotan.com", Username = "info@nichotan.com", Password = "pw-info" }), EmailSenderKind.Info);
        p.Username.Should().Be("info@nichotan.com");
        p.Password.Should().Be("pw-info");
    }

    [Theory]
    [InlineData("info@nichotan.com", null)]
    [InlineData(null, "pw-info")]
    public void A_half_given_login_is_ignored_rather_than_mixing_one_mailboxs_name_with_anothers_password(string? username, string? password)
    {
        var p = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "info@nichotan.com", Username = username, Password = password }), EmailSenderKind.Info);
        p.Username.Should().Be("DoNotReply@nichotan.com");
        p.Password.Should().Be("pw-auto");
    }

    [Fact]
    public void Info_can_override_the_server_and_the_display_name()
    {
        var p = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "info@nichotan.com", Host = "smtp.other.example", Port = 465, EnableSsl = false, FromName = "Nichotan Team" }), EmailSenderKind.Info);
        (p.Host, p.Port, p.EnableSsl, p.FromName).Should().Be(("smtp.other.example", 465, false, "Nichotan Team"));
    }

    [Fact]
    public void Automated_mail_is_unaffected_by_the_info_settings()
    {
        var p = SmtpProfile.For(Options(o => o.Info = new SmtpSenderOverride { FromAddress = "info@nichotan.com", Username = "info@nichotan.com", Password = "pw-info" }), EmailSenderKind.Automated);
        p.FromAddress.Should().Be("DoNotReply@nichotan.com");
        p.Username.Should().Be("DoNotReply@nichotan.com");
    }
}

public class AcsSenderAddressTests
{
    private static AcsEmailOptions Options(string? info = "info@nichotan.com", string? replyTo = "info@nichotan.com") =>
        new() { FromAddress = "DoNotReply@nichotan.com", InfoFromAddress = info, ReplyTo = replyTo };

    [Fact]
    public void Each_kind_uses_its_own_address_and_only_automated_mail_needs_a_reply_to()
    {
        var o = Options();
        o.AddressFor(EmailSenderKind.Automated).Should().Be("DoNotReply@nichotan.com");
        o.AddressFor(EmailSenderKind.Info).Should().Be("info@nichotan.com");
        o.ReplyToFor(EmailSenderKind.Automated).Should().Be("info@nichotan.com");
        o.ReplyToFor(EmailSenderKind.Info).Should().BeNull();
    }

    [Fact]
    public void Without_an_info_address_everything_goes_from_the_automated_one()
    {
        var o = Options(info: " ");
        o.AddressFor(EmailSenderKind.Info).Should().Be("DoNotReply@nichotan.com");
        o.ReplyToFor(EmailSenderKind.Info).Should().Be("info@nichotan.com", "it is still an automated sender, so replies are directed to Info");
    }
}

public class EmailTemplateSenderTests
{
    private const string Link = "https://app.nichotan.com/x";

    /// <summary>
    /// The rule: mail to a student about their own case, where they might reasonably reply, is sent from Info.
    /// Account links and every notice to staff are automated. Changing a template's sender must be a deliberate choice, so it fails here first.
    /// </summary>
    public static TheoryData<string, EmailMessage, EmailSenderKind> Cases => new()
    {
        { nameof(EmailTemplates.VerifyEmail), EmailTemplates.VerifyEmail("a@b.c", "A", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.ResetPassword), EmailTemplates.ResetPassword("a@b.c", "A", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.StaffInvitation), EmailTemplates.StaffInvitation("a@b.c", "A", "B", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.NewEnquiry), EmailTemplates.NewEnquiry("a@b.c", "A", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.ApplicationSent), EmailTemplates.ApplicationSent("a@b.c", "A", "School", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.DocumentResubmitted), EmailTemplates.DocumentResubmitted("a@b.c", "A", "Passport", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.OfferLetterUploaded), EmailTemplates.OfferLetterUploaded("a@b.c", "A", "School", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.StaffDigest), EmailTemplates.StaffDigest("a@b.c", 1, 0, 0, Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.BiometricsDone), EmailTemplates.BiometricsDone("a@b.c", "A", Link), EmailSenderKind.Automated },
        { nameof(EmailTemplates.FormReopened), EmailTemplates.FormReopened("a@b.c", "A", Link), EmailSenderKind.Info },
        { nameof(EmailTemplates.DocumentNeedsAttention), EmailTemplates.DocumentNeedsAttention("a@b.c", "A", Link), EmailSenderKind.Info },
        { nameof(EmailTemplates.OfferAvailable), EmailTemplates.OfferAvailable("a@b.c", "A", Link), EmailSenderKind.Info },
        { nameof(EmailTemplates.VisaSubmitted), EmailTemplates.VisaSubmitted("a@b.c", "A", Link), EmailSenderKind.Info },
        { nameof(EmailTemplates.PreDepartureReady), EmailTemplates.PreDepartureReady("a@b.c", "A", Link), EmailSenderKind.Info },
        { nameof(EmailTemplates.BiometricsReminder), EmailTemplates.BiometricsReminder("a@b.c", "A", Link), EmailSenderKind.Info },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_template_uses_the_intended_sender(string template, EmailMessage message, EmailSenderKind expected)
        => message.From.Should().Be(expected, $"{template} should be sent from the {expected} address");

    [Fact]
    public void Every_template_is_covered_by_the_table_above()
    {
        // Adding a template without deciding its sender fails here.
        var templates = typeof(EmailTemplates).GetMethods().Where(m => m.IsStatic && m.ReturnType == typeof(EmailMessage)).Select(m => m.Name).ToHashSet();
        var covered = Cases.Select(c => (string)c[0]).ToHashSet();
        templates.Except(covered).Should().BeEmpty("every email template needs an explicit sender decision");
    }

    [Fact]
    public void A_message_built_without_a_choice_is_automated()
        => new EmailMessage("a@b.c", "s", "t", "<p>h</p>").From.Should().Be(EmailSenderKind.Automated);
}
