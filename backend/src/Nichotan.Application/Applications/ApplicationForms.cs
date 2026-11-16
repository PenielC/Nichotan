using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Applications;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Applications;

// ─────────────────────────── Results ───────────────────────────

public record FormVersionSummary(int VersionNumber, DateTime SubmittedAtUtc, string SignerName, string ContentHash);

public record ApplicationFormResult(
    Guid? Id,
    Guid CandidateId,
    FormStatus Status,
    bool IsEditable,
    ApplicationFormData Data,
    ApplicantLevel? ApplicantLevel,
    int CurrentVersion,
    DateTime? SubmittedAtUtc,
    string? ReopenReason,
    DateTime? ReopenedAtUtc,
    string DeclarationVersion,
    string DeclarationText,
    IReadOnlyList<FormVersionSummary> Versions);

public record SignatureResult(
    string Provider,
    string SignerName,
    DateTime SignedAtUtc,
    string? IpAddress,
    string? ImageDataUrl,
    string? ExternalReference,
    string DeclarationVersion,
    string DeclarationText);

public record FormVersionResult(Guid CandidateId, int VersionNumber, ApplicationFormData Data, ApplicantLevel? ApplicantLevel, DateTime SubmittedAtUtc, string ContentHash, SignatureResult Signature);

public record ApplicationFormSummary(
    Guid CandidateId,
    string CandidateName,
    string Email,
    FormStatus Status,
    int CurrentVersion,
    DestinationCountry? DestinationCountry,
    ApplicantLevel? ApplicantLevel,
    DateTime? SubmittedAtUtc,
    DateTime LastChangedAtUtc);

internal static class FormMapping
{
    public static ApplicantLevel? LevelOf(ApplicationFormData data) =>
        data.Program.ProgramLevel is { } level ? ProgramLevels.ApplicantLevelOf(level) : null;

    public static async Task<IReadOnlyList<FormVersionSummary>> VersionSummaries(IAppDbContext db, Guid formId, CancellationToken cancellationToken) =>
        await db.ApplicationFormVersions.AsNoTracking()
            .Where(v => v.FormId == formId)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new FormVersionSummary(v.VersionNumber, v.SubmittedAtUtc, v.Signature.SignerName, v.ContentHash))
            .ToListAsync(cancellationToken);

    public static ApplicationFormResult ToResult(ApplicationForm form, IReadOnlyList<FormVersionSummary> versions)
    {
        var data = FormJson.Deserialize(form.DataJson);
        return new ApplicationFormResult(
            form.Id, form.CandidateId, form.Status, form.IsEditable, data, LevelOf(data), form.CurrentVersion, form.SubmittedAtUtc,
            form.ReopenReason, form.ReopenedAtUtc, Declaration.CurrentVersion, Declaration.Text, versions);
    }

    /// <summary>What the form looks like before anything is saved: the details already known from the account.</summary>
    public static ApplicationFormResult EmptyFor(CandidateProfile profile) => new(
        null, profile.Id, FormStatus.Draft, true, Prefill(profile), null, 0, null, null, null, Declaration.CurrentVersion, Declaration.Text, []);

    public static ApplicationFormData Prefill(CandidateProfile profile) => new()
    {
        Personal = { FirstName = profile.FirstName, LastName = profile.LastName },
        Contact = { Email = profile.Email, Phone = profile.Phone },
    };

    public static FormVersionResult ToResult(ApplicationFormVersion v, Guid candidateId)
    {
        var data = FormJson.Deserialize(v.DataJson);
        var s = v.Signature;
        var image = s.Image is { Length: > 0 } ? "data:image/png;base64," + Convert.ToBase64String(s.Image) : null;
        return new FormVersionResult(
            candidateId, v.VersionNumber, data, LevelOf(data), v.SubmittedAtUtc, v.ContentHash,
            new SignatureResult(s.Provider, s.SignerName, s.SignedAtUtc, s.IpAddress, image, s.ExternalReference, s.DeclarationVersion, s.DeclarationText));
    }
}

// ─────────────────────────── Signing ───────────────────────────

public record SignatureRequest(string SignerName, string? SignatureImage, string ContentHash, string? IpAddress, DateTime NowUtc, string DeclarationVersion, string DeclarationText);

/// <summary>
/// Captures the candidate's signature on the declaration. The built-in implementation keeps a drawn signature
/// with a timestamp and IP address; a third-party e-signature provider can replace it by implementing this
/// interface and registering it instead.
/// </summary>
public interface IDeclarationSigner
{
    Task<SignatureEvidence> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default);
}

public class NativeDeclarationSigner : IDeclarationSigner
{
    public const string ProviderName = "Native";
    private const int MaxImageBytes = 200 * 1024;
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public Task<SignatureEvidence> SignAsync(SignatureRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SignerName)) throw new DomainException("Type your full name to sign.");
        if (string.IsNullOrWhiteSpace(request.SignatureImage)) throw new DomainException("Draw your signature to sign.");

        var base64 = request.SignatureImage;
        var comma = base64.IndexOf(',');
        if (base64.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0) base64 = base64[(comma + 1)..];

        byte[] image;
        try { image = Convert.FromBase64String(base64); }
        catch (FormatException) { throw new DomainException("The signature couldn't be read. Draw it again."); }

        if (image.Length > MaxImageBytes) throw new DomainException("The signature image is too large.");
        if (image.Length < 100 || !image.AsSpan(0, PngHeader.Length).SequenceEqual(PngHeader)) throw new DomainException("The signature couldn't be read. Draw it again.");

        return Task.FromResult(new SignatureEvidence(
            ProviderName, request.SignerName.Trim(), request.NowUtc, request.IpAddress, image, externalReference: null, request.DeclarationVersion, request.DeclarationText));
    }
}

// ─────────────────────────── PDF ───────────────────────────

/// <summary>Renders a submitted version as the branded, printable Nichotan form.</summary>
public interface IApplicationFormPdf
{
    byte[] Render(FormVersionResult version);
}

public record FormPdfResult(byte[] Content, string FileName);

public record GetMyFormPdfQuery(int VersionNumber) : IQuery<FormPdfResult>;

public class GetMyFormPdfHandler(ISender sender, IApplicationFormPdf pdf) : IQueryHandler<GetMyFormPdfQuery, FormPdfResult>
{
    public async ValueTask<FormPdfResult> Handle(GetMyFormPdfQuery query, CancellationToken cancellationToken)
    {
        var version = await sender.Send(new GetMyFormVersionQuery(query.VersionNumber), cancellationToken);
        return new FormPdfResult(pdf.Render(version), FileNameFor(version));
    }

    internal static string FileNameFor(FormVersionResult v) => $"Nichotan-application-form-v{v.VersionNumber}.pdf";
}

public record GetApplicationFormPdfQuery(Guid CandidateId, int VersionNumber) : IQuery<FormPdfResult>;

/// <summary>Staff download. Goes through the version query, so it is authorized and audited the same way.</summary>
public class GetApplicationFormPdfHandler(ISender sender, IApplicationFormPdf pdf) : IQueryHandler<GetApplicationFormPdfQuery, FormPdfResult>
{
    public async ValueTask<FormPdfResult> Handle(GetApplicationFormPdfQuery query, CancellationToken cancellationToken)
    {
        var version = await sender.Send(new GetApplicationFormVersionQuery(query.CandidateId, query.VersionNumber), cancellationToken);
        return new FormPdfResult(pdf.Render(version), GetMyFormPdfHandler.FileNameFor(version));
    }
}

// ─────────────────────────── Validation ───────────────────────────

internal static partial class FormRules
{
    public const int MaxInstitutions = 5;
    public const int MaxProgramChoices = 2;
    public const int MaxHighSchools = 2;
    public const int MaxPostSecondary = 3;

    [GeneratedRegex(@"^(19|20)\d{2}-(0[1-9]|1[0-2])$")]
    public static partial Regex YearMonth();

    public static bool IsEmail(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(value);
}

/// <summary>Shape rules that apply to a draft too: sizes, lengths and formats of whatever has been filled in.</summary>
public class SaveMyApplicationDraftValidator : AbstractValidator<SaveMyApplicationDraftCommand>
{
    public SaveMyApplicationDraftValidator()
    {
        RuleFor(x => x.Data).NotNull();
        When(x => x.Data is not null, () =>
        {
            RuleFor(x => x.Data.Personal.FirstName).MaximumLength(100);
            RuleFor(x => x.Data.Personal.MiddleNames).MaximumLength(100);
            RuleFor(x => x.Data.Personal.LastName).MaximumLength(100);
            RuleFor(x => x.Data.Personal.CitizenshipCountry).MaximumLength(100);
            RuleFor(x => x.Data.Personal.BirthDate).Must(d => d is null || (d.Value.Year >= 1900 && d.Value <= DateOnly.FromDateTime(DateTime.UtcNow))).WithMessage("Enter a valid date of birth.");

            RuleFor(x => x.Data.Contact.HomeAddress).MaximumLength(300);
            RuleFor(x => x.Data.Contact.City).MaximumLength(100);
            RuleFor(x => x.Data.Contact.Country).MaximumLength(100);
            RuleFor(x => x.Data.Contact.Email).MaximumLength(256);
            RuleFor(x => x.Data.Contact.Phone).MaximumLength(30);
            RuleFor(x => x.Data.Contact.AlternativePhone).MaximumLength(30);
            RuleFor(x => x.Data.Contact.NextOfKinName).MaximumLength(150);
            RuleFor(x => x.Data.Contact.NextOfKinEmail).MaximumLength(256);
            RuleFor(x => x.Data.Contact.NextOfKinPhone).MaximumLength(30);

            RuleFor(x => x.Data.Program.StartYear).Must(y => y is null || (y >= 2000 && y <= 2100)).WithMessage("Enter a valid year.");
            RuleFor(x => x.Data.Program.Institutions).Must(l => l.Count <= FormRules.MaxInstitutions).WithMessage($"List at most {FormRules.MaxInstitutions} institutions.");
            RuleFor(x => x.Data.Program.ProgramChoices).Must(l => l.Count <= FormRules.MaxProgramChoices).WithMessage($"List at most {FormRules.MaxProgramChoices} programmes.");
            RuleForEach(x => x.Data.Program.Institutions).MaximumLength(200);
            RuleForEach(x => x.Data.Program.ProgramChoices).MaximumLength(200);

            RuleFor(x => x.Data.Academic.HighSchools).Must(l => l.Count <= FormRules.MaxHighSchools).WithMessage($"List at most {FormRules.MaxHighSchools} high schools.");
            RuleFor(x => x.Data.Academic.PostSecondary).Must(l => l.Count <= FormRules.MaxPostSecondary).WithMessage($"List at most {FormRules.MaxPostSecondary} institutions.");
            RuleForEach(x => x.Data.Academic.HighSchools).ChildRules(h =>
            {
                h.RuleFor(e => e.Name).MaximumLength(200);
                h.RuleFor(e => e.ProvinceCountry).MaximumLength(150);
                h.RuleFor(e => e.Started).Must(MonthOrEmpty).WithMessage("Use mm/yyyy.");
                h.RuleFor(e => e.Completed).Must(MonthOrEmpty).WithMessage("Use mm/yyyy.");
            });
            RuleForEach(x => x.Data.Academic.PostSecondary).ChildRules(p =>
            {
                p.RuleFor(e => e.Institution).MaximumLength(200);
                p.RuleFor(e => e.ProvinceCountry).MaximumLength(150);
                p.RuleFor(e => e.CredentialAwarded).MaximumLength(200);
                p.RuleFor(e => e.Started).Must(MonthOrEmpty).WithMessage("Use mm/yyyy.");
                p.RuleFor(e => e.Completed).Must(MonthOrEmpty).WithMessage("Use mm/yyyy.");
                p.RuleFor(e => e.CredentialAwardedOn).Must(MonthOrEmpty).WithMessage("Use mm/yyyy.");
            });
        });
    }

    private static bool MonthOrEmpty(string? value) => string.IsNullOrWhiteSpace(value) || FormRules.YearMonth().IsMatch(value);
}

/// <summary>Completeness rules checked when the candidate submits. What's required follows the applicant's level.</summary>
public class ApplicationFormSubmissionValidator : AbstractValidator<ApplicationFormData>
{
    public ApplicationFormSubmissionValidator()
    {
        RuleFor(x => x.Personal.FirstName).NotEmpty();
        RuleFor(x => x.Personal.LastName).NotEmpty();
        RuleFor(x => x.Personal.Gender).NotNull();
        RuleFor(x => x.Personal.BirthDate).NotNull().WithMessage("Enter your date of birth.");
        RuleFor(x => x.Personal.CitizenshipCountry).NotEmpty();

        RuleFor(x => x.Contact.HomeAddress).NotEmpty();
        RuleFor(x => x.Contact.City).NotEmpty();
        RuleFor(x => x.Contact.Country).NotEmpty();
        RuleFor(x => x.Contact.Email).Must(FormRules.IsEmail).WithMessage("Enter a valid email address.");
        RuleFor(x => x.Contact.Phone).NotEmpty().MustBeInternationalNumber();
        RuleFor(x => x.Contact.AlternativePhone).MustBeInternationalNumber();
        RuleFor(x => x.Contact.NextOfKinPhone).MustBeInternationalNumber();
        RuleFor(x => x.Contact.NextOfKinName).NotEmpty();
        RuleFor(x => x.Contact.NextOfKinEmail).Must(FormRules.IsEmail).WithMessage("Enter a valid email address.");

        RuleFor(x => x.Program.DestinationCountry).NotNull().WithMessage("Choose Canada or Germany.");
        RuleFor(x => x.Program.ProgramLevel).NotNull().WithMessage("Choose your program level.");
        RuleFor(x => x.Program.StartYear).NotNull().Must(y => y is null || y >= DateTime.UtcNow.Year).WithMessage("Enter the year you want to start.");
        RuleFor(x => x.Program.StartTerm).NotNull().WithMessage("Choose when you want to start.");
        RuleFor(x => x.Program.Institutions).Must(l => l.Any(i => !string.IsNullOrWhiteSpace(i))).WithMessage("Name at least one institution.");
        RuleFor(x => x.Program.ProgramChoices).Must(l => l.Any(i => !string.IsNullOrWhiteSpace(i))).WithMessage("Name at least one programme.");

        // High school and undergraduate applicants are assessed on their schooling; postgraduate applicants on prior post-secondary study.
        When(x => x.Program.ProgramLevel is { } level && ProgramLevels.ApplicantLevelOf(level) != ApplicantLevel.Postgraduate, () =>
            RuleFor(x => x.Academic.HighSchools).Must(l => l.Count > 0).WithMessage("Add at least one high school."));
        When(x => x.Program.ProgramLevel is { } level && ProgramLevels.ApplicantLevelOf(level) == ApplicantLevel.Postgraduate, () =>
            RuleFor(x => x.Academic.PostSecondary).Must(l => l.Count > 0).WithMessage("Add at least one post-secondary institution."));

        RuleForEach(x => x.Academic.HighSchools).ChildRules(h =>
        {
            h.RuleFor(e => e.Name).NotEmpty();
            h.RuleFor(e => e.ProvinceCountry).NotEmpty();
            h.RuleFor(e => e.Started).NotEmpty().Must(v => v is null || FormRules.YearMonth().IsMatch(v)).WithMessage("Use mm/yyyy.");
            h.RuleFor(e => e.Grade).NotNull();
            h.RuleFor(e => e).Must(e => string.IsNullOrWhiteSpace(e.Completed) || string.IsNullOrWhiteSpace(e.Started) || string.CompareOrdinal(e.Completed, e.Started) >= 0)
                .WithMessage("The completion date can't be before the start date.");
        });
        RuleForEach(x => x.Academic.PostSecondary).ChildRules(p =>
        {
            p.RuleFor(e => e.Institution).NotEmpty();
            p.RuleFor(e => e.ProvinceCountry).NotEmpty();
            p.RuleFor(e => e.Started).NotEmpty().Must(v => v is null || FormRules.YearMonth().IsMatch(v)).WithMessage("Use mm/yyyy.");
            p.RuleFor(e => e).Must(e => string.IsNullOrWhiteSpace(e.Completed) || string.IsNullOrWhiteSpace(e.Started) || string.CompareOrdinal(e.Completed, e.Started) >= 0)
                .WithMessage("The completion date can't be before the start date.");
        });
    }
}

// ─────────────────────────── The candidate's own form ───────────────────────────

public record GetMyApplicationFormQuery : IQuery<ApplicationFormResult>;

public class GetMyApplicationFormHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyApplicationFormQuery, ApplicationFormResult>
{
    public async ValueTask<ApplicationFormResult> Handle(GetMyApplicationFormQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        var form = await db.ApplicationForms.AsNoTracking().FirstOrDefaultAsync(f => f.CandidateId == profile.Id, cancellationToken);
        return form is null ? FormMapping.EmptyFor(profile) : FormMapping.ToResult(form, await FormMapping.VersionSummaries(db, form.Id, cancellationToken));
    }
}

public record SaveMyApplicationDraftCommand(ApplicationFormData Data) : ICommand<ApplicationFormResult>;

public class SaveMyApplicationDraftHandler(IAppDbContext db, ICurrentUser current) : ICommandHandler<SaveMyApplicationDraftCommand, ApplicationFormResult>
{
    public async ValueTask<ApplicationFormResult> Handle(SaveMyApplicationDraftCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        var form = await db.ApplicationForms.FirstOrDefaultAsync(f => f.CandidateId == profile.Id, cancellationToken);
        if (form is null)
        {
            form = new ApplicationForm(profile.Id, command.Data);
            db.ApplicationForms.Add(form);
        }
        else
        {
            form.SaveDraft(command.Data);
        }

        await db.SaveChangesAsync(cancellationToken);
        return FormMapping.ToResult(form, await FormMapping.VersionSummaries(db, form.Id, cancellationToken));
    }
}

/// <summary>Submits the saved form with the candidate's signature. The form is locked afterwards.</summary>
public record SubmitMyApplicationCommand(string SignerName, string? SignatureImage, bool AcceptDeclaration, string DeclarationVersion) : ICommand<ApplicationFormResult>;

public class SubmitMyApplicationValidator : AbstractValidator<SubmitMyApplicationCommand>
{
    public SubmitMyApplicationValidator()
    {
        RuleFor(x => x.SignerName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.SignatureImage).NotEmpty().MaximumLength(400_000);
        RuleFor(x => x.AcceptDeclaration).Equal(true).WithMessage("Tick the box to confirm the declaration.");
    }
}

public class SubmitMyApplicationHandler(
    IAppDbContext db,
    ICurrentUser current,
    IAuditLog audit,
    IClock clock,
    IDeclarationSigner signer,
    IValidator<ApplicationFormData> completeness) : ICommandHandler<SubmitMyApplicationCommand, ApplicationFormResult>
{
    public async ValueTask<ApplicationFormResult> Handle(SubmitMyApplicationCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        var form = await db.ApplicationForms.FirstOrDefaultAsync(f => f.CandidateId == profile.Id, cancellationToken)
            ?? throw new DomainException("Fill in and save your form before submitting it.");
        if (!form.IsEditable) throw new DomainException("This form has already been submitted.");
        if (command.DeclarationVersion != Declaration.CurrentVersion) throw new DomainException("The declaration has been updated. Reload the page and read it again.");

        var data = FormJson.Deserialize(form.DataJson);
        var result = await completeness.ValidateAsync(data, cancellationToken);
        if (!result.IsValid) throw new RequestValidationException(result.Errors);

        var now = clock.UtcNow;
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{form.DataJson}\n{Declaration.CurrentVersion}\n{Declaration.Text}"))).ToLowerInvariant();
        var signature = await signer.SignAsync(
            new SignatureRequest(command.SignerName, command.SignatureImage, contentHash, current.IpAddress, now, Declaration.CurrentVersion, Declaration.Text), cancellationToken);

        var version = form.Submit(userId, now, contentHash, signature);
        db.ApplicationFormVersions.Add(version);
        audit.Add(AuditAction.ApplicationFormSubmitted, entityType: nameof(ApplicationForm), entityId: form.Id.ToString(), details: new { version = version.VersionNumber });
        await db.SaveChangesAsync(cancellationToken);
        return FormMapping.ToResult(form, await FormMapping.VersionSummaries(db, form.Id, cancellationToken));
    }
}

public record GetMyFormVersionQuery(int VersionNumber) : IQuery<FormVersionResult>;

public class GetMyFormVersionHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyFormVersionQuery, FormVersionResult>
{
    public async ValueTask<FormVersionResult> Handle(GetMyFormVersionQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        var version = await (from v in db.ApplicationFormVersions.AsNoTracking()
                             join f in db.ApplicationForms.AsNoTracking() on v.FormId equals f.Id
                             where f.CandidateId == profile.Id && v.VersionNumber == query.VersionNumber
                             select v).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Version not found.");
        return FormMapping.ToResult(version, profile.Id);
    }
}

// ─────────────────────────── Staff ───────────────────────────

public record ListApplicationFormsQuery(FormStatus? Status, string? Search, int Page = 1, int PageSize = 25) : IQuery<PagedResult<ApplicationFormSummary>>;

public class ListApplicationFormsValidator : AbstractValidator<ListApplicationFormsQuery>
{
    public ListApplicationFormsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class ListApplicationFormsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListApplicationFormsQuery, PagedResult<ApplicationFormSummary>>
{
    public async ValueTask<PagedResult<ApplicationFormSummary>> Handle(ListApplicationFormsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var rows = from f in db.ApplicationForms.AsNoTracking()
                   join p in db.CandidateProfiles.AsNoTracking() on f.CandidateId equals p.Id
                   select new { Form = f, Profile = p };
        if (query.Status is { } status) rows = rows.Where(r => r.Form.Status == status);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            rows = rows.Where(r => (r.Profile.FirstName + " " + r.Profile.LastName).ToLower().Contains(term) || r.Profile.Email.Contains(term));
        }

        var total = await rows.CountAsync(cancellationToken);
        var page = await rows.OrderByDescending(r => r.Form.UpdatedAtUtc ?? r.Form.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(r => new { r.Form.CandidateId, r.Profile.FirstName, r.Profile.LastName, r.Profile.Email, r.Form.Status, r.Form.CurrentVersion, r.Form.DataJson, r.Form.SubmittedAtUtc, Changed = r.Form.UpdatedAtUtc ?? r.Form.CreatedAtUtc })
            .ToListAsync(cancellationToken);

        var items = page.Select(r =>
        {
            var data = FormJson.Deserialize(r.DataJson);
            return new ApplicationFormSummary(r.CandidateId, $"{r.FirstName} {r.LastName}", r.Email, r.Status, r.CurrentVersion, data.Program.DestinationCountry, FormMapping.LevelOf(data), r.SubmittedAtUtc, r.Changed);
        }).ToList();
        return new PagedResult<ApplicationFormSummary>(items, query.Page, query.PageSize, total);
    }
}

public record GetApplicationFormQuery(Guid CandidateId) : IQuery<ApplicationFormResult>;

/// <summary>Opening a candidate's form is logged: it holds personal data.</summary>
public class GetApplicationFormHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : IQueryHandler<GetApplicationFormQuery, ApplicationFormResult>
{
    public async ValueTask<ApplicationFormResult> Handle(GetApplicationFormQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var form = await db.ApplicationForms.AsNoTracking().FirstOrDefaultAsync(f => f.CandidateId == query.CandidateId, cancellationToken)
            ?? throw new NotFoundException("This candidate hasn't started their form.");
        audit.Add(AuditAction.ApplicationFormViewed, entityType: nameof(ApplicationForm), entityId: form.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return FormMapping.ToResult(form, await FormMapping.VersionSummaries(db, form.Id, cancellationToken));
    }
}

public record GetApplicationFormVersionQuery(Guid CandidateId, int VersionNumber) : IQuery<FormVersionResult>;

public class GetApplicationFormVersionHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit) : IQueryHandler<GetApplicationFormVersionQuery, FormVersionResult>
{
    public async ValueTask<FormVersionResult> Handle(GetApplicationFormVersionQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var version = await (from v in db.ApplicationFormVersions.AsNoTracking()
                             join f in db.ApplicationForms.AsNoTracking() on v.FormId equals f.Id
                             where f.CandidateId == query.CandidateId && v.VersionNumber == query.VersionNumber
                             select v).FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Version not found.");
        audit.Add(AuditAction.ApplicationFormViewed, entityType: nameof(ApplicationForm), entityId: version.FormId.ToString(), details: new { version = version.VersionNumber });
        await db.SaveChangesAsync(cancellationToken);
        return FormMapping.ToResult(version, query.CandidateId);
    }
}

public record ReopenApplicationFormCommand(Guid CandidateId, string Reason) : ICommand<ApplicationFormResult>;

public class ReopenApplicationFormValidator : AbstractValidator<ReopenApplicationFormCommand>
{
    public ReopenApplicationFormValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class ReopenApplicationFormHandler(
    IAppDbContext db,
    ICurrentUser current,
    IAuditLog audit,
    IClock clock,
    IEmailSender email,
    IAppUrls urls,
    ILogger<ReopenApplicationFormHandler> logger) : ICommandHandler<ReopenApplicationFormCommand, ApplicationFormResult>
{
    public async ValueTask<ApplicationFormResult> Handle(ReopenApplicationFormCommand command, CancellationToken cancellationToken)
    {
        var staffId = current.RequireAny(Guard.CaseWorkers);
        var form = await db.ApplicationForms.FirstOrDefaultAsync(f => f.CandidateId == command.CandidateId, cancellationToken)
            ?? throw new NotFoundException("This candidate hasn't started their form.");
        var profile = await db.CandidateProfiles.AsNoTracking().FirstAsync(p => p.Id == form.CandidateId, cancellationToken);

        form.Reopen(staffId, command.Reason, clock.UtcNow);
        audit.Add(AuditAction.ApplicationFormReopened, entityType: nameof(ApplicationForm), entityId: form.Id.ToString(), details: new { version = form.CurrentVersion });
        await db.SaveChangesAsync(cancellationToken);

        // The email carries a link only; the reason is shown after signing in.
        await RegisterCandidateHandler.SendSafely(email, EmailTemplates.FormReopened(profile.Email, profile.FirstName, $"{urls.WebBaseUrl}/portal/application"), logger, cancellationToken);
        return FormMapping.ToResult(form, await FormMapping.VersionSummaries(db, form.Id, cancellationToken));
    }
}
