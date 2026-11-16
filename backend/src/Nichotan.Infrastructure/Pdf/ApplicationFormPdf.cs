using System.Globalization;
using System.Reflection;
using Nichotan.Application.Applications;
using Nichotan.Domain.Applications;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nichotan.Infrastructure.Pdf;

/// <summary>The Nichotan application form as a branded, printable PDF, laid out like the paper form with the signed declaration at the end.</summary>
public class ApplicationFormPdf : IApplicationFormPdf
{
    private const string Ink = "#0d1b3e";
    private const string Leaf = "#8cbf26";
    private const string Muted = "#5b6478";
    private const string Rule = "#d5dae6";

    private static readonly byte[] Crest = LoadCrest();

    static ApplicationFormPdf()
    {
        // QuestPDF's Community licence: free for companies under US$1M annual gross revenue. Review before go-live
        // (see https://www.questpdf.com/license/); a larger company needs a paid licence and a different value here.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(FormVersionResult version)
    {
        var d = version.Data;
        var s = version.Signature;

        return Document.Create(document => document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(32);
            page.DefaultTextStyle(t => t.FontSize(9.5f).FontColor(Ink));

            page.Header().PaddingBottom(10).BorderBottom(1.5f).BorderColor(Leaf).Row(row =>
            {
                row.ConstantItem(46).Image(Crest).FitArea();
                row.RelativeItem().PaddingLeft(10).Column(c =>
                {
                    c.Item().Text("NICHO TAN CONSULTING").FontSize(15).Bold();
                    c.Item().Text("Your Global Education Partner").FontSize(9).FontColor(Muted);
                });
                row.AutoItem().AlignRight().Column(c =>
                {
                    c.Item().AlignRight().Text("APPLICATION FORM").FontSize(13).Bold();
                    c.Item().AlignRight().Text($"Version {version.VersionNumber} · submitted {version.SubmittedAtUtc:dd/MM/yyyy}").FontSize(8.5f).FontColor(Muted);
                });
            });

            page.Content().PaddingVertical(8).Column(col =>
            {
                col.Spacing(6);

                Section(col, "Personal information");
                Fields(col, ("First name", d.Personal.FirstName), ("Middle name(s)", d.Personal.MiddleNames), ("Last or family name", d.Personal.LastName));
                Fields(col, ("Gender", d.Personal.Gender?.ToString()), ("Birthdate (dd/mm/yyyy)", d.Personal.BirthDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)), ("Country of citizenship", d.Personal.CitizenshipCountry));

                Section(col, "Contact information");
                Fields(col, ("Home address", d.Contact.HomeAddress), ("City", d.Contact.City), ("Country", d.Contact.Country));
                Fields(col, ("Email", d.Contact.Email), ("Phone number", d.Contact.Phone), ("Alternative phone", d.Contact.AlternativePhone));
                Fields(col, ("Next of kin (full name)", d.Contact.NextOfKinName), ("Next of kin email", d.Contact.NextOfKinEmail), ("Next of kin phone", d.Contact.NextOfKinPhone));

                Section(col, "Program and institution selection");
                Fields(col, ("Destination country", d.Program.DestinationCountry?.ToString()), ("Program level", Level(d.Program.ProgramLevel)), ("Applicant type", version.ApplicantLevel?.ToString()));
                Fields(col, ("Start year", d.Program.StartYear?.ToString(CultureInfo.InvariantCulture)), ("Start term", Term(d.Program.StartTerm)));
                Numbered(col, "Institution (University/College)", d.Program.Institutions);
                Numbered(col, "Program choice", d.Program.ProgramChoices);

                Section(col, "Academic history");
                col.Item().Text("High schools attended, most recent first").FontColor(Muted).FontSize(8.5f);
                if (d.Academic.HighSchools.Count == 0) col.Item().Text("None listed").FontColor(Muted);
                for (var i = 0; i < d.Academic.HighSchools.Count; i++)
                {
                    var h = d.Academic.HighSchools[i];
                    Fields(col, ($"{i + 1}. Name", h.Name), ("Province, Country", h.ProvinceCountry), ("Attended", Range(h.Started, h.Completed)), ("Grade", h.Grade is { } g ? (g == HighSchoolGrade.LessThan12 ? "Less than 12" : "12 or equivalent (A level)") : null));
                }

                col.Item().PaddingTop(4).Text("Post-secondary institutions attended, most recent first").FontColor(Muted).FontSize(8.5f);
                if (d.Academic.PostSecondary.Count == 0) col.Item().Text("None listed").FontColor(Muted);
                for (var i = 0; i < d.Academic.PostSecondary.Count; i++)
                {
                    var p = d.Academic.PostSecondary[i];
                    Fields(col, ($"{i + 1}. Institution", p.Institution), ("Province, Country", p.ProvinceCountry), ("Attended", Range(p.Started, p.Completed)), ("Credential awarded", Credential(p)));
                }

                Section(col, "Consent for disclosure and declaration of applicant");
                col.Item().Text(s.DeclarationText).Italic();
                col.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem(3).Column(c =>
                    {
                        c.Item().Height(64).BorderBottom(0.7f).BorderColor(Ink).AlignBottom().Element(e => SignatureImage(e, s.ImageDataUrl));
                        c.Item().PaddingTop(2).Text($"Signature of applicant: {s.SignerName}").FontSize(8.5f).FontColor(Muted);
                    });
                    row.ConstantItem(24);
                    row.RelativeItem(1.4f).Column(c =>
                    {
                        c.Item().Height(64).BorderBottom(0.7f).BorderColor(Ink).AlignBottom().Text(s.SignedAtUtc.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).FontSize(11);
                        c.Item().PaddingTop(2).Text("Date (dd/mm/yyyy)").FontSize(8.5f).FontColor(Muted);
                    });
                });
            });

            page.Footer().BorderTop(0.5f).BorderColor(Rule).PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text($"Signed electronically ({s.Provider}) on {s.SignedAtUtc:dd/MM/yyyy HH:mm} UTC · declaration v{s.DeclarationVersion} · content hash {version.ContentHash[..16]}…")
                    .FontSize(7.5f).FontColor(Muted);
                row.AutoItem().Text(t =>
                {
                    t.DefaultTextStyle(x => x.FontSize(7.5f).FontColor(Muted));
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        })).GeneratePdf();
    }

    private static void Section(ColumnDescriptor col, string title) =>
        col.Item().PaddingTop(6).Background(Ink).PaddingVertical(3).PaddingHorizontal(6).Text(title.ToUpperInvariant()).FontColor(Colors.White).Bold().FontSize(9);

    private static void Fields(ColumnDescriptor col, params (string Label, string? Value)[] items) =>
        col.Item().Row(row =>
        {
            foreach (var (label, value) in items)
            {
                row.RelativeItem().PaddingRight(8).Column(c =>
                {
                    c.Item().Text(label).FontSize(7.5f).FontColor(Muted);
                    c.Item().BorderBottom(0.5f).BorderColor(Rule).PaddingBottom(2).Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
                });
            }
        });

    private static void Numbered(ColumnDescriptor col, string label, List<string> values)
    {
        var filled = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
        if (filled.Count == 0) Fields(col, (label, null));
        for (var i = 0; i < filled.Count; i++) Fields(col, ($"{label} {i + 1}", filled[i]));
    }

    private static void SignatureImage(IContainer container, string? dataUrl)
    {
        try
        {
            var comma = dataUrl?.IndexOf(',') ?? -1;
            if (dataUrl is null || comma < 0) throw new FormatException();
            container.Image(Image.FromBinaryData(Convert.FromBase64String(dataUrl[(comma + 1)..]))).FitArea();
        }
        catch (Exception)
        {
            container.Text("(signature image unavailable)").FontColor(Muted).FontSize(8);
        }
    }

    private static string? Level(ProgramLevel? level) => level switch
    {
        ProgramLevel.HighSchool => "High school",
        ProgramLevel.Bachelors => "Bachelor's degree",
        ProgramLevel.Diploma => "Diploma",
        ProgramLevel.Certificate => "Certificate",
        ProgramLevel.Masters => "Graduate degree (Master's)",
        ProgramLevel.PostGraduateDiplomaOrCertificate => "Post Graduate Diploma/Certificate",
        _ => null,
    };

    private static string? Term(StartTerm? term) => term switch
    {
        StartTerm.Winter => "Winter (Jan-Apr)",
        StartTerm.Summer => "Summer (May-Aug)",
        StartTerm.Fall => "Fall (Sep-Dec)",
        _ => null,
    };

    /// <summary>"2016-01" → "01/2016".</summary>
    private static string? Month(string? value) => value is { Length: 7 } && value[4] == '-' ? $"{value[5..]}/{value[..4]}" : value;

    private static string? Range(string? start, string? end) =>
        string.IsNullOrWhiteSpace(start) ? null : $"{Month(start)} – {(string.IsNullOrWhiteSpace(end) ? "present" : Month(end))}";

    private static string? Credential(PostSecondaryEntry p) =>
        string.IsNullOrWhiteSpace(p.CredentialAwarded) ? null : string.IsNullOrWhiteSpace(p.CredentialAwardedOn) ? p.CredentialAwarded : $"{p.CredentialAwarded} ({Month(p.CredentialAwardedOn)})";

    private static byte[] LoadCrest()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("crest.png") ?? throw new InvalidOperationException("The crest image is missing from the build.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
