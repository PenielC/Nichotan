using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Common;
using Nichotan.Domain.Identity;
using Nichotan.Domain.Messaging;

namespace Nichotan.Application.Messaging;

public record MessageLogEntry(Guid Id, MessageChannel Channel, string Subject, string Recipient, MessageState State, string? Error, DateTime AttemptedAtUtc, string Sender);

public record MessageLogResult(IReadOnlyList<MessageLogEntry> Items, int Sent, int Failed);

/// <summary>The latest message attempts, newest first, for administrators (BR-11.3). Counts cover everything in the filter, the list its newest 100.</summary>
public record ListMessageLogQuery(MessageState? State) : IQuery<MessageLogResult>;

public class ListMessageLogHandler(IAppDbContext db, ICurrentUser current) : IQueryHandler<ListMessageLogQuery, MessageLogResult>
{
    public async ValueTask<MessageLogResult> Handle(ListMessageLogQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny([Roles.Administrator]);
        var all = db.MessageLogs.AsNoTracking();
        var items = await (query.State is { } state ? all.Where(m => m.State == state) : all)
            .OrderByDescending(m => m.AttemptedAtUtc)
            .Take(100)
            .Select(m => new MessageLogEntry(m.Id, m.Channel, m.Subject, m.Recipient, m.State, m.Error, m.AttemptedAtUtc, m.Sender))
            .ToListAsync(cancellationToken);
        var sent = await all.CountAsync(m => m.State == MessageState.Sent, cancellationToken);
        var failed = await all.CountAsync(m => m.State == MessageState.Failed, cancellationToken);
        return new MessageLogResult(items, sent, failed);
    }
}
