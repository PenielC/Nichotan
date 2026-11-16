using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Documents;
using Nichotan.Domain.Identity;
using Nichotan.Domain.Visas;
using Nichotan.Infrastructure.Identity;

namespace Nichotan.Infrastructure.Persistence;

public class BootstrapOptions
{
    /// <summary>Applies pending migrations at startup. Production runs migrations as a release step instead.</summary>
    public bool MigrateOnStartup { get; set; }

    /// <summary>First administrator, created only when no administrator exists yet. Set via secrets, never committed for real environments.</summary>
    public string? AdminEmail { get; set; }

    public string? AdminPassword { get; set; }
    public string AdminFirstName { get; set; } = "Nichotan";
    public string AdminLastName { get; set; } = "Administrator";
}

public class DbInitializer(
    AppDbContext db,
    RoleManager<IdentityRole<Guid>> roles,
    UserManager<AppUser> users,
    IIdentityService identity,
    IOptions<BootstrapOptions> options,
    ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var o = options.Value;
        if (o.MigrateOnStartup) await db.Database.MigrateAsync(cancellationToken);

        await SeedChecklistAsync(cancellationToken);
        await EnsureSystemDocumentTypesAsync(cancellationToken);
        await SeedVisaChecklistAsync(cancellationToken);
        await SeedPreDepartureAsync(cancellationToken);

        foreach (var role in Roles.All)
        {
            if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole<Guid>(role));
        }

        if (string.IsNullOrWhiteSpace(o.AdminEmail) || string.IsNullOrWhiteSpace(o.AdminPassword)) return;
        if ((await users.GetUsersInRoleAsync(Roles.Administrator)).Count > 0) return;

        await identity.CreateAsync(o.AdminEmail, o.AdminPassword, o.AdminFirstName, o.AdminLastName, [Roles.Administrator], emailConfirmed: true, cancellationToken);
        logger.LogInformation("Created the first administrator account");
    }

    /// <summary>
    /// The starting checklist from the BRD (6.2), added only when none exists yet. Nichotan then edits it: the final
    /// Canada and Germany lists, and any institution-specific rules, are entered by an administrator.
    /// </summary>
    private async Task SeedChecklistAsync(CancellationToken cancellationToken)
    {
        if (await db.DocumentTypes.AnyAsync(cancellationToken)) return;

        var passport = new DocumentType("Passport", "A clear scan of the photo page.", DocumentScope.Candidate, tracksExpiry: true, isAutomatic: false, sortOrder: 1);
        var records = new DocumentType("Academic records", "Certificates and transcripts for your schooling so far.", DocumentScope.Candidate, false, false, 2);
        var form = new DocumentType("Nichotan application form", "Met when you sign and submit your application form.", DocumentScope.Candidate, false, isAutomatic: true, 3);
        var cv = new DocumentType("CV", "Your curriculum vitae.", DocumentScope.Candidate, false, false, 4);
        var academicReference = new DocumentType("Academic reference", "A reference letter from a lecturer or teacher.", DocumentScope.Candidate, false, false, 5);
        var professionalReference = new DocumentType("Professional reference", "A reference letter from an employer or supervisor.", DocumentScope.Candidate, false, false, 6);
        var purpose = new DocumentType("Statement of purpose", "Why you want to study this programme. Written for each school.", DocumentScope.Application, false, false, 7);
        db.DocumentTypes.AddRange(passport, records, form, cv, academicReference, professionalReference, purpose);

        // Required for everyone.
        foreach (var t in new[] { passport, records, form }) db.DocumentRequirements.Add(new DocumentRequirement(t.Id, null, null, null, isRequired: true));
        // Required for postgraduate applicants; for others only where an institution-specific line says so.
        foreach (var t in new[] { cv, academicReference, professionalReference, purpose }) db.DocumentRequirements.Add(new DocumentRequirement(t.Id, null, ApplicantLevel.Postgraduate, null, isRequired: true));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the starting document checklist");
    }

    /// <summary>The document types behind the school's letters. They aren't checklist items, so they are added whether or not a checklist was seeded.</summary>
    private async Task EnsureSystemDocumentTypesAsync(CancellationToken cancellationToken)
    {
        var wanted = new (DocumentPurpose Purpose, string Name, string Description, int Order)[]
        {
            (DocumentPurpose.OfferLetter, "Offer letter", "The school's offer, as sent by the school.", 100),
            (DocumentPurpose.AcceptanceLetter, "Formal acceptance letter", "The school's formal acceptance letter.", 102),
            (DocumentPurpose.PalLetter, "PAL letter", "The Provincial Attestation Letter that came with the acceptance.", 103),
            (DocumentPurpose.BiometricsLetter, "Biometrics letter", "The authority's letter asking for biometrics, as received.", 110),
            (DocumentPurpose.DecisionLetter, "Visa decision letter", "The authority's decision on the visa, as received.", 111),
        };
        var existing = await db.DocumentTypes.Where(t => t.Purpose != DocumentPurpose.None).Select(t => t.Purpose).ToListAsync(cancellationToken);
        var added = false;
        foreach (var w in wanted.Where(w => !existing.Contains(w.Purpose)))
        {
            db.DocumentTypes.Add(new DocumentType(w.Name, w.Description, DocumentScope.Application, tracksExpiry: false, isAutomatic: false, w.Order, w.Purpose));
            added = true;
        }

        if (added) await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// A starting visa checklist, added only when none exists yet. It is a draft for Nichotan to correct: the real
    /// Canada and Germany lists must be confirmed by Nichotan and kept current before go-live.
    /// </summary>
    private async Task SeedVisaChecklistAsync(CancellationToken cancellationToken)
    {
        if (await db.DocumentRequirements.AnyAsync(r => r.Stage == ChecklistStage.Visa, cancellationToken)) return;

        var types = await db.DocumentTypes.ToListAsync(cancellationToken);
        DocumentType Type(string name, string description, DocumentScope scope, int order, bool tracksExpiry = false)
        {
            var existing = types.FirstOrDefault(t => t.Name == name);
            if (existing is not null) return existing;
            var created = new DocumentType(name, description, scope, tracksExpiry, isAutomatic: false, order);
            types.Add(created);
            db.DocumentTypes.Add(created);
            return created;
        }

        var passport = Type("Passport", "A clear scan of the photo page.", DocumentScope.Candidate, 1, tracksExpiry: true);
        var photo = Type("Passport-style photo", "A recent photo that meets the visa photo rules.", DocumentScope.Candidate, 20);
        var funds = Type("Proof of funds", "Evidence you can pay for fees and living costs, such as bank statements.", DocumentScope.Candidate, 21);
        var police = Type("Police clearance certificate", "A police certificate from each country you have lived in.", DocumentScope.Candidate, 22);
        var medical = Type("Medical examination", "The result of your medical examination, if one is required.", DocumentScope.Candidate, 23);
        var studyPlan = Type("Study plan", "Why you want to study this programme and how it fits your plans.", DocumentScope.Application, 24);
        var fee = Type("Visa fee receipt", "Proof that the visa application fee has been paid.", DocumentScope.Application, 25);
        var blocked = Type("Blocked account confirmation", "Confirmation from your blocked account provider.", DocumentScope.Candidate, 26);
        var insurance = Type("Health insurance proof", "Proof of health insurance cover for your studies.", DocumentScope.Candidate, 27);
        await db.SaveChangesAsync(cancellationToken);

        void Line(DocumentType t, DestinationCountry? country) => db.DocumentRequirements.Add(new DocumentRequirement(t.Id, country, null, null, isRequired: true, ChecklistStage.Visa));
        foreach (var t in new[] { passport, photo, fee }) Line(t, null);
        foreach (var t in new[] { funds, police, medical, studyPlan }) Line(t, DestinationCountry.Canada);
        foreach (var t in new[] { blocked, insurance }) Line(t, DestinationCountry.Germany);

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the starting visa checklist");
    }

    /// <summary>
    /// A starting pre-departure list for each country, added only when none exists. It is a draft: Nichotan must approve the
    /// lists and the wording shown with them, and keep them in line with each country's current rules.
    /// </summary>
    private async Task SeedPreDepartureAsync(CancellationToken cancellationToken)
    {
        if (await db.PreDepartureTemplate.AnyAsync(cancellationToken)) return;

        var order = 0;
        void Item(DestinationCountry country, string title, string? detail, PreDepartureImportance importance = PreDepartureImportance.Required) =>
            db.PreDepartureTemplate.Add(new PreDepartureTemplateItem(country, title, detail, importance, order += 10));

        order = 0;
        Item(DestinationCountry.Canada, "Passport with your visa", "Valid for your whole stay. Keep it in your hand luggage.");
        Item(DestinationCountry.Canada, "Study permit approval letter", "The letter that came with your approval. Print it.");
        Item(DestinationCountry.Canada, "Original academic certificates", "The originals, not copies.");
        Item(DestinationCountry.Canada, "Valid acceptance letter", "Your school's formal acceptance letter.");
        Item(DestinationCountry.Canada, "Proof of fees paid", "Receipts for the fees you've paid to the school.");
        Item(DestinationCountry.Canada, "Proof of accommodation", "Where you'll stay when you arrive.");
        Item(DestinationCountry.Canada, "Proof of funds", "Evidence you can pay for your studies and living costs.");
        Item(DestinationCountry.Canada, "Medical information sheet", "Your medical information, if one was asked for.");
        Item(DestinationCountry.Canada, "Police clearance", "Your police clearance certificate.");
        Item(DestinationCountry.Canada, "Warm clothing", "Advice only, not an immigration requirement: pack for the weather where you're going.", PreDepartureImportance.Recommended);

        order = 0;
        Item(DestinationCountry.Germany, "Passport with your visa", "Valid for your whole stay. Keep it in your hand luggage.");
        Item(DestinationCountry.Germany, "Admission letter from your university", "The letter confirming your place.");
        Item(DestinationCountry.Germany, "Original academic certificates", "The originals, not copies.");
        Item(DestinationCountry.Germany, "Proof of health insurance", "Your health insurance confirmation.");
        Item(DestinationCountry.Germany, "Proof of finances", "Such as your blocked account confirmation.");
        Item(DestinationCountry.Germany, "Proof of accommodation", "Where you'll stay when you arrive.");
        Item(DestinationCountry.Germany, "Warm clothing", "Advice only, not a requirement: pack for the weather where you're going.", PreDepartureImportance.Recommended);

        const string Caveat = "This list is a guide from Nichotan, not legal advice or a guarantee. Rules change, so check the latest requirements with the visa authority and your school before you travel.";
        db.PreDepartureCaveats.Add(new PreDepartureCaveat(DestinationCountry.Canada, Caveat));
        db.PreDepartureCaveats.Add(new PreDepartureCaveat(DestinationCountry.Germany, Caveat));

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded the starting pre-departure lists");
    }
}
