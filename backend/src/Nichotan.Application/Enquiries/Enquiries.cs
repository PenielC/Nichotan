using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Application.Identity;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Enquiries;

namespace Nichotan.Application.Enquiries;

public record EnquiryResult(
    Guid Id,
    string Name,
    string? Email,
    string? Phone,
    Qualification? HighestQualification,
    int? GraduationYear,
    string? PreferredProgramme,
    bool OpenToGuidance,
    DestinationInterest? DestinationInterest,
    string? Message,
    EnquiryStatus Status,
    string? StaffNote,
    DateTime CreatedAtUtc,
    DateTime? StatusChangedAtUtc,
    string? HandledBy,
    DateTime? ViewedAtUtc,
    string? ViewedBy,
    EnquirySource Source)
{
    public static EnquiryResult From(Enquiry e) =>
        new(e.Id, e.Name, e.Email, e.Phone, e.HighestQualification, e.GraduationYear, e.PreferredProgramme, e.OpenToGuidance, e.DestinationInterest, e.Message, e.Status, e.StaffNote, e.CreatedAtUtc, e.StatusChangedAtUtc, e.HandledByEmail, e.FirstViewedAtUtc, e.FirstViewedByEmail, e.Source);
}

// ─────────────────────────── The public form ───────────────────────────

public record SubmitEnquiryCommand(
    string Name,
    string? Email,
    string? Phone,
    Qualification HighestQualification,
    int GraduationYear,
    string? PreferredProgramme,
    bool OpenToGuidance,
    DestinationInterest DestinationInterest,
    string? Message) : ICommand;

public class SubmitEnquiryValidator : AbstractValidator<SubmitEnquiryCommand>
{
    public SubmitEnquiryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30).MustBeInternationalNumber();
        RuleFor(x => x.HighestQualification).IsInEnum();
        RuleFor(x => x.DestinationInterest).IsInEnum();
        // Someone may still be studying, so a graduation year a few years ahead is fine.
        RuleFor(x => x.GraduationYear).InclusiveBetween(1950, DateTime.UtcNow.Year + 8).WithMessage("Enter the year you graduated, or expect to.");
        RuleFor(x => x.PreferredProgramme).MaximumLength(200);
        RuleFor(x => x.Message).MaximumLength(2000);
    }
}

/// <summary>
/// Open to anyone, so it only saves what was sent. The domain checks that we can reach them and know what they want.
/// The team is then emailed (a failure to send never loses the enquiry), and the sender is not emailed back.
/// </summary>
public class SubmitEnquiryHandler(IAppDbContext db, IEmailSender email, IAppUrls urls, IOptions<EnquiryOptions> options, ILogger<SubmitEnquiryHandler> logger) : ICommandHandler<SubmitEnquiryCommand>
{
    public async ValueTask<Unit> Handle(SubmitEnquiryCommand command, CancellationToken cancellationToken)
    {
        var enquiry = new Enquiry(command.Name, command.Email, PhoneNumbers.Normalise(command.Phone), command.HighestQualification, command.GraduationYear, command.PreferredProgramme, command.OpenToGuidance, command.DestinationInterest, command.Message);
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.Value.NotifyEmail))
        {
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.NewEnquiry(options.Value.NotifyEmail.Trim(), enquiry.Name, $"{urls.WebBaseUrl}/staff/enquiries"), logger, cancellationToken);
        }

        return Unit.Value;
    }
}

// ─────────────────────────── The website's "Send a Message" form ───────────────────────────

public record SubmitWebsiteMessageCommand(string Name, string? Email, string? Phone, string Message) : ICommand;

public class SubmitWebsiteMessageValidator : AbstractValidator<SubmitWebsiteMessageCommand>
{
    public SubmitWebsiteMessageValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(30).MustBeInternationalNumber();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>
/// A message from the public website lands in the same list as every other enquiry, marked with where it came from,
/// and the team is emailed. As with the landing-page form, a failure to send never loses the message.
/// </summary>
public class SubmitWebsiteMessageHandler(IAppDbContext db, IEmailSender email, IAppUrls urls, IOptions<EnquiryOptions> options, ILogger<SubmitWebsiteMessageHandler> logger) : ICommandHandler<SubmitWebsiteMessageCommand>
{
    public async ValueTask<Unit> Handle(SubmitWebsiteMessageCommand command, CancellationToken cancellationToken)
    {
        var enquiry = Enquiry.FromWebsiteMessage(command.Name, command.Email, PhoneNumbers.Normalise(command.Phone), command.Message);
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(options.Value.NotifyEmail))
        {
            await RegisterCandidateHandler.SendSafely(email, EmailTemplates.NewEnquiry(options.Value.NotifyEmail.Trim(), enquiry.Name, $"{urls.WebBaseUrl}/staff/enquiries"), logger, cancellationToken);
        }

        return Unit.Value;
    }
}

// ─────────────────────────── Staff ───────────────────────────

public record ListEnquiriesQuery(EnquiryStatus? Status, string? Search, bool UnviewedOnly = false, int Page = 1, int PageSize = 25) : IQuery<PagedResult<EnquiryResult>>;

public class ListEnquiriesValidator : AbstractValidator<ListEnquiriesQuery>
{
    public ListEnquiriesValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class ListEnquiriesHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListEnquiriesQuery, PagedResult<EnquiryResult>>
{
    public async ValueTask<PagedResult<EnquiryResult>> Handle(ListEnquiriesQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var enquiries = db.Enquiries.AsNoTracking().AsQueryable();
        if (query.Status is { } status) enquiries = enquiries.Where(e => e.Status == status);
        if (query.UnviewedOnly) enquiries = enquiries.Where(e => e.FirstViewedAtUtc == null);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            enquiries = enquiries.Where(e => e.Name.ToLower().Contains(term) || (e.Email != null && e.Email.Contains(term)) || (e.Phone != null && e.Phone.Contains(term)));
        }

        var total = await enquiries.CountAsync(cancellationToken);
        var page = await enquiries.OrderBy(e => e.FirstViewedAtUtc != null).ThenBy(e => e.Status).ThenByDescending(e => e.CreatedAtUtc).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<EnquiryResult>(page.Select(EnquiryResult.From).ToList(), query.Page, query.PageSize, total);
    }
}

public record UpdateEnquiryCommand(Guid Id, EnquiryStatus Status, string? StaffNote) : ICommand<EnquiryResult>;

public class UpdateEnquiryValidator : AbstractValidator<UpdateEnquiryCommand>
{
    public UpdateEnquiryValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.StaffNote).MaximumLength(2000);
    }
}

public class UpdateEnquiryHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<UpdateEnquiryCommand, EnquiryResult>
{
    public async ValueTask<EnquiryResult> Handle(UpdateEnquiryCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var enquiry = await db.Enquiries.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Enquiry not found.");
        var from = enquiry.Status;
        enquiry.Update(command.Status, command.StaffNote, current.Email, clock.UtcNow);
        audit.Add(AuditAction.EnquiryUpdated, entityType: nameof(Enquiry), entityId: enquiry.Id.ToString(), details: new { from = from.ToString(), to = command.Status.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        return EnquiryResult.From(enquiry);
    }
}

/// <summary>Opening an enquiry for review. The first administrator or counsellor to do so is recorded; others viewing doesn't change it.</summary>
public record MarkEnquiryViewedCommand(Guid Id) : ICommand<EnquiryResult>;

public class MarkEnquiryViewedHandler(IAppDbContext db, ICurrentUser current, IAuditLog audit, IClock clock) : ICommandHandler<MarkEnquiryViewedCommand, EnquiryResult>
{
    public async ValueTask<EnquiryResult> Handle(MarkEnquiryViewedCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.AdmissionsStaff);
        var enquiry = await db.Enquiries.FirstOrDefaultAsync(e => e.Id == command.Id, cancellationToken) ?? throw new NotFoundException("Enquiry not found.");
        if (enquiry.MarkViewed(current.Email, clock.UtcNow))
        {
            audit.Add(AuditAction.EnquiryViewed, entityType: nameof(Enquiry), entityId: enquiry.Id.ToString());
            await db.SaveChangesAsync(cancellationToken);
        }

        return EnquiryResult.From(enquiry);
    }
}

public record EnquirySummary(int Unviewed, int New, IReadOnlyList<EnquiryResult> Latest);

/// <summary>For the dashboard and menu: how many enquiries nobody has opened yet, and the newest of them.</summary>
public record GetEnquirySummaryQuery : IQuery<EnquirySummary>;

public class GetEnquirySummaryHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetEnquirySummaryQuery, EnquirySummary>
{
    public async ValueTask<EnquirySummary> Handle(GetEnquirySummaryQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var enquiries = db.Enquiries.AsNoTracking();
        var unviewed = await enquiries.CountAsync(e => e.FirstViewedAtUtc == null, cancellationToken);
        var fresh = await enquiries.CountAsync(e => e.Status == EnquiryStatus.New, cancellationToken);
        var latest = await enquiries.Where(e => e.FirstViewedAtUtc == null).OrderByDescending(e => e.CreatedAtUtc).Take(5).ToListAsync(cancellationToken);
        return new EnquirySummary(unviewed, fresh, latest.Select(EnquiryResult.From).ToList());
    }
}
