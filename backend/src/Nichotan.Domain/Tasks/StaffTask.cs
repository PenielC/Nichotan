using Nichotan.Domain.Common;

namespace Nichotan.Domain.Tasks;

public enum TaskPriority { Low, Normal, High }

public enum StaffTaskStatus { Open, Done, Cancelled }

/// <summary>
/// Something a staff member needs to do: assigned to one person, with a priority and a due day, and optionally tied to a student,
/// an admission application or a visa case. Students never see tasks.
/// </summary>
public class StaffTask : Entity
{
    private StaffTask() { }

    public StaffTask(string title, string? notes, TaskPriority priority, DateOnly? dueOn, Guid assigneeUserId, Guid? candidateId, Guid? applicationId, Guid? visaCaseId, Guid createdByUserId, string? createdByEmail)
    {
        Status = StaffTaskStatus.Open;
        AssigneeUserId = assigneeUserId;
        CandidateId = candidateId;
        ApplicationId = applicationId;
        VisaCaseId = visaCaseId;
        CreatedByUserId = createdByUserId;
        CreatedByEmail = createdByEmail;
        Update(title, notes, priority, dueOn, assigneeUserId);
    }

    public string Title { get; private set; } = string.Empty;
    public string? Notes { get; private set; }
    public TaskPriority Priority { get; private set; }
    public StaffTaskStatus Status { get; private set; }

    /// <summary>The day it should be done by, if there is one.</summary>
    public DateOnly? DueOn { get; private set; }

    public Guid AssigneeUserId { get; private set; }
    public Guid? CandidateId { get; private set; }
    public Guid? ApplicationId { get; private set; }
    public Guid? VisaCaseId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string? CreatedByEmail { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string? CompletedByEmail { get; private set; }

    public bool IsOverdue(DateOnly today) => Status == StaffTaskStatus.Open && DueOn is { } due && due < today;

    public void Update(string title, string? notes, TaskPriority priority, DateOnly? dueOn, Guid assigneeUserId)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Say what needs doing.");
        if (Status != StaffTaskStatus.Open && Title.Length > 0) throw new DomainException("Reopen this task before changing it.");
        Title = title.Trim();
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Priority = priority;
        DueOn = dueOn;
        AssigneeUserId = assigneeUserId;
    }

    public void Complete(string? staffEmail, DateTime nowUtc)
    {
        if (Status != StaffTaskStatus.Open) throw new DomainException("This task isn't open.");
        Status = StaffTaskStatus.Done;
        CompletedAtUtc = nowUtc;
        CompletedByEmail = staffEmail;
    }

    public void Cancel(string? staffEmail, DateTime nowUtc)
    {
        if (Status != StaffTaskStatus.Open) throw new DomainException("This task isn't open.");
        Status = StaffTaskStatus.Cancelled;
        CompletedAtUtc = nowUtc;
        CompletedByEmail = staffEmail;
    }

    public void Reopen()
    {
        if (Status == StaffTaskStatus.Open) throw new DomainException("This task is already open.");
        Status = StaffTaskStatus.Open;
        CompletedAtUtc = null;
        CompletedByEmail = null;
    }
}
