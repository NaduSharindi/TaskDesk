namespace TaskDesk.Domain;

/// <summary>
/// An office to-do item. All state changes go through methods so the workflow rules
/// (allowed transitions, read-only when closed) cannot be bypassed by a controller.
/// </summary>
public class TaskItem
{
    public const int TitleMaxLength = 150;
    public const int DescriptionMaxLength = 2000;
    public const int CommentMaxLength = 1000;
    public const string WebSource = "Web";

    private static readonly Dictionary<WorkStatus, WorkStatus[]> Transitions = new()
    {
        [WorkStatus.ToDo] = [WorkStatus.InProgress, WorkStatus.Blocked, WorkStatus.Done, WorkStatus.Cancelled],
        [WorkStatus.InProgress] = [WorkStatus.ToDo, WorkStatus.Blocked, WorkStatus.Done, WorkStatus.Cancelled],
        [WorkStatus.Blocked] = [WorkStatus.InProgress, WorkStatus.Cancelled],
        [WorkStatus.Done] = [WorkStatus.InProgress],
        [WorkStatus.Cancelled] = [WorkStatus.ToDo],
    };

    private readonly List<TaskComment> _comments = [];

    // Required by EF Core.
    private TaskItem()
    {
    }

    public TaskItem(
        string title,
        string? description,
        TaskPriority priority,
        DateOnly? dueDate,
        int departmentId,
        string? assignedToId,
        string? createdById,
        string createdByName,
        string source,
        string? externalReference,
        DateTime nowUtc)
    {
        SetDetails(title, description, priority, dueDate);
        DepartmentId = departmentId;
        AssignedToId = assignedToId;
        CreatedById = createdById;
        CreatedByName = createdByName;
        Source = source;
        ExternalReference = externalReference;
        Status = WorkStatus.ToDo;
        CreatedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    public int Id { get; private set; }

    /// <summary>Human-friendly reference shown in the UI and e-mails, e.g. TD-00042.</summary>
    public string Reference => FormatReference(Id);

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public TaskPriority Priority { get; private set; }

    public WorkStatus Status { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public int DepartmentId { get; private set; }

    public Department? Department { get; private set; }

    public string? AssignedToId { get; private set; }

    public AppUser? AssignedTo { get; private set; }

    public string? CreatedById { get; private set; }

    public AppUser? CreatedBy { get; private set; }

    public string CreatedByName { get; private set; } = string.Empty;

    /// <summary>"Web" or "API:{client}" - where the task came from.</summary>
    public string Source { get; private set; } = WebSource;

    /// <summary>Caller's own ID (e.g. helpdesk ticket number). Unique per Source = idempotent API creates.</summary>
    public string? ExternalReference { get; private set; }

    /// <summary>Optimistic concurrency token, rotated on every change.</summary>
    public Guid ConcurrencyStamp { get; private set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<TaskComment> Comments => _comments.AsReadOnly();

    public bool IsOpen => Status is not (WorkStatus.Done or WorkStatus.Cancelled);

    public IReadOnlyList<WorkStatus> AllowedTransitions => Transitions[Status];

    public static string FormatReference(int id) => $"TD-{id:D5}";

    public bool IsOverdue(DateOnly today) => IsOpen && DueDate is { } due && due < today;

    public bool CanMoveTo(WorkStatus target) => Transitions[Status].Contains(target);

    public void UpdateDetails(string title, string? description, TaskPriority priority, DateOnly? dueDate, DateTime nowUtc)
    {
        EnsureEditable();
        SetDetails(title, description, priority, dueDate);
        Touch(nowUtc);
    }

    public void AssignTo(string? userId, int departmentId, DateTime nowUtc)
    {
        EnsureEditable();
        AssignedToId = userId;
        DepartmentId = departmentId;
        Touch(nowUtc);
    }

    public void ChangeStatus(WorkStatus target, DateTime nowUtc)
    {
        if (!CanMoveTo(target))
        {
            throw new BusinessRuleException($"A task cannot move from '{Status.ToDisplay()}' to '{target.ToDisplay()}'.");
        }

        Status = target;
        CompletedAtUtc = target == WorkStatus.Done ? nowUtc : null;
        Touch(nowUtc);
    }

    public TaskComment AddComment(string? authorId, string authorName, string text, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new BusinessRuleException("A comment cannot be empty.");
        }

        if (text.Length > CommentMaxLength)
        {
            throw new BusinessRuleException($"A comment cannot exceed {CommentMaxLength} characters.");
        }

        var comment = new TaskComment(Id, authorId, authorName, text.Trim(), nowUtc);
        _comments.Add(comment);
        UpdatedAtUtc = nowUtc;
        return comment;
    }

    private void SetDetails(string title, string? description, TaskPriority priority, DateOnly? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BusinessRuleException("Title is required.");
        }

        if (title.Trim().Length > TitleMaxLength)
        {
            throw new BusinessRuleException($"Title cannot exceed {TitleMaxLength} characters.");
        }

        if (description?.Length > DescriptionMaxLength)
        {
            throw new BusinessRuleException($"Description cannot exceed {DescriptionMaxLength} characters.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new BusinessRuleException("Unknown priority.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Priority = priority;
        DueDate = dueDate;
    }

    private void EnsureEditable()
    {
        if (!IsOpen)
        {
            throw new BusinessRuleException("Completed or cancelled tasks are read-only. Reopen the task first.");
        }
    }

    private void Touch(DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        ConcurrencyStamp = Guid.NewGuid();
    }
}
