using System.Net;

namespace Nichotan.Application.Common;

/// <summary>
/// Account emails. They contain links only: no documents, passport or financial details (BRD 10).
/// </summary>
public static class EmailTemplates
{
    public static EmailMessage VerifyEmail(string to, string firstName, string link) => Build(
        to,
        "Confirm your email for Nichotan",
        firstName,
        "Thanks for creating your Nichotan account. Please confirm your email address so we can keep you updated about your applications.",
        "Confirm email",
        link,
        "This link expires in 48 hours. If you didn't create an account, you can ignore this email.");

    public static EmailMessage ResetPassword(string to, string firstName, string link) => Build(
        to,
        "Reset your Nichotan password",
        firstName,
        "We received a request to reset your Nichotan password.",
        "Choose a new password",
        link,
        "This link expires in 1 hour and can be used once. If you didn't ask for this, you can ignore this email: your password hasn't changed.");

    public static EmailMessage StaffInvitation(string to, string firstName, string invitedBy, string link) => Build(
        to,
        "You've been invited to the Nichotan staff portal",
        firstName,
        $"{invitedBy} has created a Nichotan staff account for you. Set your password to get started; you'll then be asked to set up an authenticator app.",
        "Set your password",
        link,
        "This invitation expires in 72 hours.");

    public static EmailMessage FormReopened(string to, string firstName, string link) => Build(
        to,
        "Your Nichotan application form needs a few changes",
        firstName,
        "Your counsellor has reopened your application form so you can correct it. Sign in to see what needs changing, update the form and sign it again.",
        "Open my application form",
        link,
        "Your earlier submission is kept on record.");

    public static EmailMessage DocumentNeedsAttention(string to, string firstName, string link) => Build(
        to,
        "A document needs to be uploaded again",
        firstName,
        "Your counsellor has looked at one of your documents and needs you to upload it again. Sign in to see what to fix.",
        "Open my documents",
        link,
        "Your earlier upload is kept on record.");

    /// <summary>
    /// Tells the team an enquiry has arrived. Like every email here it carries a link, not the person's details: the
    /// name is all it says, with control characters removed since it was typed by a stranger.
    /// </summary>
    public static EmailMessage NewEnquiry(string to, string enquirerName, string link)
    {
        var name = new string(enquirerName.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length > 80) name = name[..80];
        return Build(
            to,
            "New enquiry on the Nichotan website",
            "team",
            $"{name} has sent an enquiry through the website. Open the enquiries page to read it and follow up.",
            "Review the enquiry",
            link,
            "You're receiving this because enquiries are sent to this address.");
    }

    /// <summary>
    /// Tells the team a student has finished an application and sent it for review. A link, with only the student's name
    /// and the school: no contact details or documents.
    /// </summary>
    public static EmailMessage ApplicationSent(string to, string studentName, string school, string link) => Build(
        to,
        "An application is ready for review",
        "team",
        $"{Clean(studentName, 80)} has sent an application to {Clean(school, 120)} for review. Check their documents, then apply on the school's website.",
        "Review the application",
        link,
        "You're receiving this because applications sent for review are announced to this address.");

    /// <summary>A student has sent back a document staff asked to see again. Link, student's name and the kind of document only.</summary>
    public static EmailMessage DocumentResubmitted(string to, string studentName, string documentName, string link) => Build(
        to,
        "A student has sent a document again",
        "team",
        $"{Clean(studentName, 80)} has uploaded {Clean(documentName, 120)} again after it was sent back. It is waiting for a decision.",
        "Open the student's record",
        link,
        "You're receiving this because documents sent back for fixes are announced to this address.");

    /// <summary>A student has uploaded an offer letter. Link, student's name and the school only.</summary>
    public static EmailMessage OfferLetterUploaded(string to, string studentName, string school, string link) => Build(
        to,
        "A student has uploaded an offer letter",
        "team",
        $"{Clean(studentName, 80)} has uploaded an offer letter from {Clean(school, 120)}. Read it, then record the offer on the application.",
        "Open the application",
        link,
        "You're receiving this because applications are announced to this address.");

    /// <summary>Text typed by someone else goes into an email, so control characters are removed and the length capped.</summary>
    private static string Clean(string value, int max)
    {
        var text = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return text.Length > max ? text[..max] : text;
    }

    public static EmailMessage OfferAvailable(string to, string firstName, string link) => Build(
        to,
        "You have an offer to look at",
        firstName,
        "There's news on one of your applications: your counsellor has recorded an offer. Sign in to see the details and what to do next.",
        "See my application",
        link,
        "Your counsellor will guide you through the next steps.");

    /// <summary>The visa application has been lodged: the student is reminded that biometrics come next.</summary>
    public static EmailMessage VisaSubmitted(string to, string firstName, string link) => Build(
        to,
        "Your visa application has been submitted",
        firstName,
        "Your visa application has been submitted. The next step is your biometrics. When you're asked to give them, attend your appointment, then tell us on your visa page once they're done.",
        "See my visa",
        link,
        "Your counsellor will guide you through the next steps.");

    /// <summary>The pre-departure list has been opened for an approved visa.</summary>
    public static EmailMessage PreDepartureReady(string to, string firstName, string link) => Build(
        to,
        "Your pre-departure checklist is ready",
        firstName,
        "Congratulations on your visa. Your pre-departure checklist is ready: sign in to see what to prepare and bring, and tick things off as you go.",
        "See my checklist",
        link,
        "Your counsellor will check your list with you.");

    /// <summary>A nudge to a student whose visa application is in but whose biometrics aren't marked done. Link only.</summary>
    public static EmailMessage BiometricsReminder(string to, string firstName, string link) => Build(
        to,
        "A reminder about your biometrics",
        firstName,
        "Your visa application is submitted, and we haven't heard that your biometrics are done. If you've been asked to give them, please do. Once you have, tell us on your visa page. If you've already done so, let us know there.",
        "See my visa",
        link,
        "Your counsellor is here to help if anything is unclear.");

    /// <summary>The daily note to staff: how many follow-ups are overdue. Counts and a link only, no names.</summary>
    public static EmailMessage StaffDigest(string to, int biometricsOverdue, int palOverdue, int tasksOverdue, string link)
    {
        var parts = new List<string>();
        if (biometricsOverdue > 0) parts.Add($"{biometricsOverdue} visa biometrics follow-up{(biometricsOverdue == 1 ? " is" : "s are")} overdue");
        if (palOverdue > 0) parts.Add($"{palOverdue} PAL letter{(palOverdue == 1 ? " is" : "s are")} overdue");
        if (tasksOverdue > 0) parts.Add($"{tasksOverdue} task{(tasksOverdue == 1 ? " is" : "s are")} overdue");
        return Build(
            to,
            "Follow-ups are overdue",
            "team",
            string.Join(", ", parts) + ". Open the dashboard to see which.",
            "Open the dashboard",
            link,
            "You're receiving this because applications are announced to this address.");
    }

    /// <summary>A student says their biometrics are done. Link, the student's name only.</summary>
    public static EmailMessage BiometricsDone(string to, string studentName, string link) => Build(
        to,
        "A student has done their biometrics",
        "team",
        $"{Clean(studentName, 80)} says their biometrics are done. Check, and record it on the visa case if that is right.",
        "Open the visa case",
        link,
        "You're receiving this because applications are announced to this address.");

    private static EmailMessage Build(string to, string subject, string firstName, string intro, string cta, string link, string footer)
    {
        var text = $"Hi {firstName},\n\n{intro}\n\n{cta}: {link}\n\n{footer}\n\nNichotan";
        static string e(string value) => WebUtility.HtmlEncode(value);
        var html = $"""
            <div style="font-family:Arial,sans-serif;max-width:560px;margin:auto;color:#0d1b3e">
              <p>Hi {e(firstName)},</p>
              <p>{e(intro)}</p>
              <p><a href="{e(link)}" style="display:inline-block;background:#8cbf26;color:#ffffff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:bold">{e(cta)}</a></p>
              <p style="color:#5b6478;font-size:13px">{e(footer)}</p>
              <p style="color:#5b6478;font-size:13px">Nichotan · Your global education partner</p>
            </div>
            """;
        return new EmailMessage(to, subject, text, html);
    }
}
