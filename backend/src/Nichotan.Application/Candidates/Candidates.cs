using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Candidates;
using Nichotan.Domain.Common;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Candidates;

public record CandidateProfileResult(Guid Id, Guid UserId, string FirstName, string LastName, string Email, string? Phone, DateTime CreatedAtUtc)
{
    public static CandidateProfileResult From(CandidateProfile p) => new(p.Id, p.UserId, p.FirstName, p.LastName, p.Email, p.Phone, p.CreatedAtUtc);
}

// ─────────────────────────── The candidate's own profile ───────────────────────────

public record GetMyCandidateProfileQuery : IQuery<CandidateProfileResult>;

public class GetMyCandidateProfileHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<GetMyCandidateProfileQuery, CandidateProfileResult>
{
    public async ValueTask<CandidateProfileResult> Handle(GetMyCandidateProfileQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        // Always looked up by the signed-in user, never by an id from the request.
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        return CandidateProfileResult.From(profile);
    }
}

public record UpdateMyCandidateProfileCommand(string FirstName, string LastName, string? Phone) : ICommand<CandidateProfileResult>;

public class UpdateMyCandidateProfileValidator : AbstractValidator<UpdateMyCandidateProfileCommand>
{
    public UpdateMyCandidateProfileValidator()
    {
        RuleFor(x => x.FirstName).PersonName();
        RuleFor(x => x.LastName).PersonName();
        RuleFor(x => x.Phone).MaximumLength(30).MustBeInternationalNumber();
    }
}

public class UpdateMyCandidateProfileHandler(IAppDbContext db, IIdentityService identity, IAuditLog audit, ICurrentUser current) : ICommandHandler<UpdateMyCandidateProfileCommand, CandidateProfileResult>
{
    public async ValueTask<CandidateProfileResult> Handle(UpdateMyCandidateProfileCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Roles.Candidate);
        var profile = await db.CandidateProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            ?? throw new NotFoundException("Candidate profile not found.");
        profile.Update(command.FirstName, command.LastName, PhoneNumbers.Normalise(command.Phone));
        audit.Add(AuditAction.CandidateProfileUpdated, entityType: nameof(CandidateProfile), entityId: profile.Id.ToString());

        // The account carries the name too (sign-in greetings, emails): keep both in step.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await identity.SetNameAsync(userId, profile.FirstName, profile.LastName, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CandidateProfileResult.From(profile);
    }
}

// ─────────────────────────── Staff access ───────────────────────────

public record CandidateSummary(Guid Id, string FirstName, string LastName, string Email, string? Phone, DateTime CreatedAtUtc);

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public record ListCandidatesQuery(string? Search, int Page = 1, int PageSize = 25) : IQuery<PagedResult<CandidateSummary>>;

public class ListCandidatesValidator : AbstractValidator<ListCandidatesQuery>
{
    public ListCandidatesValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(100);
    }
}

public class ListCandidatesHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListCandidatesQuery, PagedResult<CandidateSummary>>
{
    public async ValueTask<PagedResult<CandidateSummary>> Handle(ListCandidatesQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var candidates = db.CandidateProfiles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            candidates = candidates.Where(p => (p.FirstName + " " + p.LastName).ToLower().Contains(term) || p.Email.Contains(term));
        }

        var total = await candidates.CountAsync(cancellationToken);
        var items = await candidates.OrderByDescending(p => p.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(p => new CandidateSummary(p.Id, p.FirstName, p.LastName, p.Email, p.Phone, p.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        return new PagedResult<CandidateSummary>(items, query.Page, query.PageSize, total);
    }
}

public record GetCandidateQuery(Guid CandidateId) : IQuery<CandidateProfileResult>;

/// <summary>Opening a candidate's record is logged: it holds personal data.</summary>
public class GetCandidateHandler(IAppDbContext db, IAuditLog audit, ICurrentUser current) : IQueryHandler<GetCandidateQuery, CandidateProfileResult>
{
    public async ValueTask<CandidateProfileResult> Handle(GetCandidateQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var profile = await db.CandidateProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == query.CandidateId, cancellationToken)
            ?? throw new NotFoundException("Candidate not found.");
        audit.Add(AuditAction.CandidateProfileViewed, entityType: nameof(CandidateProfile), entityId: profile.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return CandidateProfileResult.From(profile);
    }
}
