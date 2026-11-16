using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Nichotan.Application.Admissions;
using Nichotan.Application.Candidates;
using Nichotan.Application.Common;
using Nichotan.Domain.Audit;
using Nichotan.Domain.Common;
using Nichotan.Domain.Tasks;

namespace Nichotan.Application.Tasks;

public record TaskResult(
    Guid Id,
    string Title,
    string? Notes,
    TaskPriority Priority,
    StaffTaskStatus Status,
    DateOnly? DueOn,
    bool Overdue,
    Guid AssigneeUserId,
    string AssigneeName,
    Guid? CandidateId,
    string? CandidateName,
    Guid? ApplicationId,
    Guid? VisaCaseId,
    string? CreatedBy,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    string? CompletedBy);

public record TaskSummary(int MineOpen, int MineOverdue, int AllOverdue);

public record Assignee(Guid Id, string Name);

internal static class TaskMapping
{
    public static async Task<Dictionary<Guid, string>> StaffNamesAsync(IIdentityService identity, CancellationToken cancellationToken) =>
        (await identity.ListAsync(staffOnly: true, cancellationToken)).ToDictionary(u => u.Id, u => u.FullName);

    public static async Task<List<TaskResult>> ToResultsAsync(IAppDbContext db, IIdentityService identity, IReadOnlyList<StaffTask> tasks, DateOnly today, CancellationToken cancellationToken)
    {
        var names = await StaffNamesAsync(identity, cancellationToken);
        var candidateIds = tasks.Where(t => t.CandidateId is not null).Select(t => t.CandidateId!.Value).Distinct().ToList();
        var candidates = await db.CandidateProfiles.AsNoTracking().Where(c => candidateIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.FirstName + " " + c.LastName, cancellationToken);
        return tasks.Select(t => new TaskResult(
            t.Id, t.Title, t.Notes, t.Priority, t.Status, t.DueOn, t.IsOverdue(today), t.AssigneeUserId, names.GetValueOrDefault(t.AssigneeUserId, "Former staff member"),
            t.CandidateId, t.CandidateId is { } c ? candidates.GetValueOrDefault(c) : null, t.ApplicationId, t.VisaCaseId, t.CreatedByEmail, t.CreatedAtUtc, t.CompletedAtUtc, t.CompletedByEmail)).ToList();
    }

    public static async Task<StaffTask> LoadAsync(IAppDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.StaffTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken) ?? throw new NotFoundException("Task not found.");

    public static async Task<TaskResult> ResultAsync(IAppDbContext db, IIdentityService identity, StaffTask task, IClock clock, CancellationToken cancellationToken) =>
        (await ToResultsAsync(db, identity, [task], AdmissionMapping.Today(clock), cancellationToken)).Single();
}

/// <summary>Staff you can give a task to: active administrators and counsellors.</summary>
public record ListAssigneesQuery : IQuery<IReadOnlyList<Assignee>>;

public class ListAssigneesHandler(IIdentityService identity, ICurrentUser current) : IQueryHandler<ListAssigneesQuery, IReadOnlyList<Assignee>>
{
    public async ValueTask<IReadOnlyList<Assignee>> Handle(ListAssigneesQuery query, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        return (await identity.ListAsync(staffOnly: true, cancellationToken)).Where(u => !u.IsDisabled).OrderBy(u => u.FirstName).ThenBy(u => u.LastName).Select(u => new Assignee(u.Id, u.FullName)).ToList();
    }
}

/// <summary>Open tasks by default, most pressing first: overdue, then by due day, then by priority.</summary>
public record ListTasksQuery(bool Mine, Guid? AssigneeUserId, StaffTaskStatus? Status, Guid? CandidateId, Guid? ApplicationId, Guid? VisaCaseId, bool OverdueOnly, int Page = 1, int PageSize = 50) : IQuery<PagedResult<TaskResult>>;

public class ListTasksValidator : AbstractValidator<ListTasksQuery>
{
    public ListTasksValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class ListTasksHandler(IAppDbContext db, ICurrentUser current, IIdentityService identity, IClock clock) : IQueryHandler<ListTasksQuery, PagedResult<TaskResult>>
{
    public async ValueTask<PagedResult<TaskResult>> Handle(ListTasksQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.CaseWorkers);
        var today = AdmissionMapping.Today(clock);
        var tasks = db.StaffTasks.AsNoTracking().AsQueryable();
        var status = query.Status ?? StaffTaskStatus.Open;
        tasks = tasks.Where(t => t.Status == status);
        if (query.Mine) tasks = tasks.Where(t => t.AssigneeUserId == userId);
        else if (query.AssigneeUserId is { } assignee) tasks = tasks.Where(t => t.AssigneeUserId == assignee);
        if (query.CandidateId is { } candidateId) tasks = tasks.Where(t => t.CandidateId == candidateId);
        if (query.ApplicationId is { } applicationId) tasks = tasks.Where(t => t.ApplicationId == applicationId);
        if (query.VisaCaseId is { } visaCaseId) tasks = tasks.Where(t => t.VisaCaseId == visaCaseId);
        if (query.OverdueOnly) tasks = tasks.Where(t => t.DueOn != null && t.DueOn < today);

        var total = await tasks.CountAsync(cancellationToken);
        // Overdue first, then the earliest due day (no due day last), then the highest priority.
        var page = await tasks
            .OrderByDescending(t => t.DueOn != null && t.DueOn < today)
            .ThenBy(t => t.DueOn == null)
            .ThenBy(t => t.DueOn)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAtUtc)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        return new PagedResult<TaskResult>(await TaskMapping.ToResultsAsync(db, identity, page, today, cancellationToken), query.Page, query.PageSize, total);
    }
}

public record GetTaskSummaryQuery : IQuery<TaskSummary>;

public class GetTaskSummaryHandler(IAppDbContext db, ICurrentUser current, IClock clock) : IQueryHandler<GetTaskSummaryQuery, TaskSummary>
{
    public async ValueTask<TaskSummary> Handle(GetTaskSummaryQuery query, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.CaseWorkers);
        var today = AdmissionMapping.Today(clock);
        var open = db.StaffTasks.AsNoTracking().Where(t => t.Status == StaffTaskStatus.Open);
        return new TaskSummary(
            await open.CountAsync(t => t.AssigneeUserId == userId, cancellationToken),
            await open.CountAsync(t => t.AssigneeUserId == userId && t.DueOn != null && t.DueOn < today, cancellationToken),
            await open.CountAsync(t => t.DueOn != null && t.DueOn < today, cancellationToken));
    }
}

public record CreateTaskCommand(string Title, string? Notes, TaskPriority Priority, DateOnly? DueOn, Guid? AssigneeUserId, Guid? CandidateId, Guid? ApplicationId, Guid? VisaCaseId) : ICommand<TaskResult>;

public class CreateTaskValidator : AbstractValidator<CreateTaskCommand>
{
    public CreateTaskValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class CreateTaskHandler(IAppDbContext db, ICurrentUser current, IIdentityService identity, IAuditLog audit, IClock clock) : ICommandHandler<CreateTaskCommand, TaskResult>
{
    public async ValueTask<TaskResult> Handle(CreateTaskCommand command, CancellationToken cancellationToken)
    {
        var userId = current.RequireAny(Guard.CaseWorkers);
        var today = AdmissionMapping.Today(clock);
        if (command.DueOn is { } due && due < today) throw new DomainException("The due day can't be in the past.");
        var assignee = command.AssigneeUserId ?? userId;
        await RequireAssigneeAsync(identity, assignee, cancellationToken);

        // A link has to point at something real, and a case has to belong to the student it is linked with.
        var candidateId = command.CandidateId;
        if (command.ApplicationId is { } applicationId)
        {
            var application = await db.AdmissionApplications.AsNoTracking().FirstOrDefaultAsync(a => a.Id == applicationId, cancellationToken) ?? throw new NotFoundException("Application not found.");
            if (candidateId is { } c && c != application.CandidateId) throw new DomainException("That application belongs to a different student.");
            candidateId = application.CandidateId;
        }

        if (command.VisaCaseId is { } visaCaseId)
        {
            var visa = await db.VisaCases.AsNoTracking().FirstOrDefaultAsync(v => v.Id == visaCaseId, cancellationToken) ?? throw new NotFoundException("Visa case not found.");
            if (candidateId is { } c && c != visa.CandidateId) throw new DomainException("That visa case belongs to a different student.");
            candidateId = visa.CandidateId;
        }

        if (candidateId is { } id && !await db.CandidateProfiles.AnyAsync(p => p.Id == id, cancellationToken)) throw new NotFoundException("Student not found.");

        var task = new StaffTask(command.Title, command.Notes, command.Priority, command.DueOn, assignee, candidateId, command.ApplicationId, command.VisaCaseId, userId, current.Email);
        db.StaffTasks.Add(task);
        audit.Add(AuditAction.TaskCreated, entityType: nameof(StaffTask), entityId: task.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await TaskMapping.ResultAsync(db, identity, task, clock, cancellationToken);
    }

    internal static async Task RequireAssigneeAsync(IIdentityService identity, Guid assignee, CancellationToken cancellationToken)
    {
        var account = await identity.FindByIdAsync(assignee, cancellationToken);
        if (account is null || !account.IsStaff || account.IsDisabled) throw new DomainException("Choose a staff member who is active.");
    }
}

public record UpdateTaskCommand(Guid Id, string Title, string? Notes, TaskPriority Priority, DateOnly? DueOn, Guid AssigneeUserId) : ICommand<TaskResult>;

public class UpdateTaskValidator : AbstractValidator<UpdateTaskCommand>
{
    public UpdateTaskValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public class UpdateTaskHandler(IAppDbContext db, ICurrentUser current, IIdentityService identity, IAuditLog audit, IClock clock) : ICommandHandler<UpdateTaskCommand, TaskResult>
{
    public async ValueTask<TaskResult> Handle(UpdateTaskCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var task = await TaskMapping.LoadAsync(db, command.Id, cancellationToken);
        // A due day that was already past stays as it was; a new one can't be set in the past.
        if (command.DueOn is { } due && due != task.DueOn && due < AdmissionMapping.Today(clock)) throw new DomainException("The due day can't be in the past.");
        if (command.AssigneeUserId != task.AssigneeUserId) await CreateTaskHandler.RequireAssigneeAsync(identity, command.AssigneeUserId, cancellationToken);
        task.Update(command.Title, command.Notes, command.Priority, command.DueOn, command.AssigneeUserId);
        audit.Add(AuditAction.TaskUpdated, entityType: nameof(StaffTask), entityId: task.Id.ToString());
        await db.SaveChangesAsync(cancellationToken);
        return await TaskMapping.ResultAsync(db, identity, task, clock, cancellationToken);
    }
}

/// <summary>Marks a task done, cancels it, or reopens one that was closed. Anyone on the staff can; the history says who.</summary>
public record ChangeTaskStatusCommand(Guid Id, string Action) : ICommand<TaskResult>;

public class ChangeTaskStatusValidator : AbstractValidator<ChangeTaskStatusCommand>
{
    public ChangeTaskStatusValidator() => RuleFor(x => x.Action).Must(a => a is "complete" or "cancel" or "reopen").WithMessage("Choose complete, cancel or reopen.");
}

public class ChangeTaskStatusHandler(IAppDbContext db, ICurrentUser current, IIdentityService identity, IAuditLog audit, IClock clock) : ICommandHandler<ChangeTaskStatusCommand, TaskResult>
{
    public async ValueTask<TaskResult> Handle(ChangeTaskStatusCommand command, CancellationToken cancellationToken)
    {
        current.RequireAny(Guard.CaseWorkers);
        var task = await TaskMapping.LoadAsync(db, command.Id, cancellationToken);
        switch (command.Action)
        {
            case "complete": task.Complete(current.Email, clock.UtcNow); break;
            case "cancel": task.Cancel(current.Email, clock.UtcNow); break;
            default: task.Reopen(); break;
        }

        audit.Add(AuditAction.TaskUpdated, entityType: nameof(StaffTask), entityId: task.Id.ToString(), details: new { action = command.Action });
        await db.SaveChangesAsync(cancellationToken);
        return await TaskMapping.ResultAsync(db, identity, task, clock, cancellationToken);
    }
}
