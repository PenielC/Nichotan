using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Identity;

namespace Nichotan.Application.Audit;

public record AuditEventResult(
    long Id,
    DateTime OccurredAtUtc,
    string Action,
    string Outcome,
    Guid? ActorUserId,
    string? ActorEmail,
    string? EntityType,
    string? EntityId,
    string? IpAddress,
    string? Details);

public record ListAuditEventsQuery(
    AuditAction? Action,
    AuditOutcome? Outcome,
    Guid? ActorUserId,
    string? EntityType,
    string? EntityId,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedResult<AuditEventResult>>;

public class ListAuditEventsValidator : AbstractValidator<ListAuditEventsQuery>
{
    public ListAuditEventsValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public class ListAuditEventsHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListAuditEventsQuery, PagedResult<AuditEventResult>>
{
    public async ValueTask<PagedResult<AuditEventResult>> Handle(ListAuditEventsQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Roles.Administrator);
        var events = db.AuditEvents.AsNoTracking();
        if (query.Action is { } action) events = events.Where(e => e.Action == action);
        if (query.Outcome is { } outcome) events = events.Where(e => e.Outcome == outcome);
        if (query.ActorUserId is { } actor) events = events.Where(e => e.ActorUserId == actor);
        if (!string.IsNullOrWhiteSpace(query.EntityType)) events = events.Where(e => e.EntityType == query.EntityType);
        if (!string.IsNullOrWhiteSpace(query.EntityId)) events = events.Where(e => e.EntityId == query.EntityId);
        if (query.FromUtc is { } from) events = events.Where(e => e.OccurredAtUtc >= from);
        if (query.ToUtc is { } to) events = events.Where(e => e.OccurredAtUtc < to);

        var total = await events.CountAsync(cancellationToken);
        var items = await events.OrderByDescending(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<AuditEventResult>(
            items.Select(e => new AuditEventResult(e.Id, e.OccurredAtUtc, e.Action.ToString(), e.Outcome.ToString(), e.ActorUserId, e.ActorEmail, e.EntityType, e.EntityId, e.IpAddress, e.Details)).ToList(),
            query.Page, query.PageSize, total);
    }
}
