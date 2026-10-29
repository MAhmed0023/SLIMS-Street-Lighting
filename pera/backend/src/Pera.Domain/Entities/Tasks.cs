namespace Pera.Domain.Entities;

/// <summary>FRD section 3.</summary>
public enum WorkTaskStatus { Draft, Assigned, InProgress, Blocked, SubmittedForReview, Returned, Completed, Cancelled }

public enum TaskPriority { Low, Normal, High, Urgent }

public class TaskCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}

public class WorkTask
{
    public int Id { get; set; }
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Purpose { get; set; }
    public int? CategoryId { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public WorkTaskStatus Status { get; set; } = WorkTaskStatus.Draft;

    public string? OwnerId { get; set; }
    public string AssignedById { get; set; } = "";
    /// <summary>Who may accept the output and close the task. Defaults to the assigning officer (FR-008).</summary>
    public string? ClosureAuthorityId { get; set; }
    public int? ParentId { get; set; }
    public int? OrgUnitId { get; set; }

    public DateTime? DueOn { get; set; }
    public string? Deliverable { get; set; }
    public string? Instructions { get; set; }
    /// <summary>Closure is refused while a linked document case is still open.</summary>
    public bool RequireDocumentsComplete { get; set; }

    public int Progress { get; set; }
    public string? ProgressNote { get; set; }
    public string? NextAction { get; set; }
    public DateTime? ForecastOn { get; set; }
    public string? Blocker { get; set; }
    public DateTime? BlockedSince { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string CreatedById { get; set; } = "";
    public DateTime? AssignedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedById { get; set; }
    public string? ClosureNote { get; set; }
    public int ReturnCount { get; set; }

    public int EscalationLevel { get; set; }
    public DateTime? RemindedAt { get; set; }

    public User? Owner { get; set; }
    public User? AssignedBy { get; set; }
    public User? ClosureAuthority { get; set; }
    public WorkTask? Parent { get; set; }
    public TaskCategory? Category { get; set; }
    public List<WorkTask> Children { get; set; } = [];
    public List<TaskActivity> Activities { get; set; } = [];
    public List<TaskAttachment> Attachments { get; set; } = [];
    public List<TaskDependency> Dependencies { get; set; } = [];

    public bool IsOpen => Status is not (WorkTaskStatus.Completed or WorkTaskStatus.Cancelled or WorkTaskStatus.Draft);
    public bool IsOverdue(DateTime utc) => IsOpen && DueOn is DateTime d && utc.Date > d.Date;
}

/// <summary>Timeline entry: status changes, progress updates, comments and replies, reassignments (FR-005/006/007).</summary>
public class TaskActivity
{
    public long Id { get; set; }
    public int TaskId { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string ActorId { get; set; } = "";
    public string? ActingForId { get; set; }
    public string Event { get; set; } = "";              // created | assigned | status | progress | comment | reassigned | attachment | dependency | escalated
    /// <summary>The task status when this entry was made — the "stage" a comment belongs to.</summary>
    public WorkTaskStatus Stage { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? Comment { get; set; }
    public long? ReplyToId { get; set; }
    public Guid? FileId { get; set; }
    /// <summary>Comma-separated user ids mentioned with @.</summary>
    public string? Mentions { get; set; }
}

public class TaskAttachment
{
    public int Id { get; set; }
    public int TaskId { get; set; }
    public Guid FileId { get; set; }
    public string Kind { get; set; } = "evidence";       // instruction | evidence | deliverable
    public string? Note { get; set; }
    public WorkTaskStatus Stage { get; set; }
    public StoredFile? File { get; set; }
}

public class TaskDependency
{
    public int TaskId { get; set; }
    public int DependsOnId { get; set; }
    public WorkTask? DependsOn { get; set; }
}
