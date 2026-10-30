namespace TaskDesk.Domain;

public enum TaskPriority
{
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>Workflow state of a task. (Named WorkStatus to avoid clashing with System.Threading.Tasks.TaskStatus.)</summary>
public enum WorkStatus
{
    ToDo = 1,
    InProgress = 2,
    Blocked = 3,
    Done = 4,
    Cancelled = 5,
}

public static class EnumDisplay
{
    public static string ToDisplay(this WorkStatus status) => status switch
    {
        WorkStatus.ToDo => "To do",
        WorkStatus.InProgress => "In progress",
        _ => status.ToString(),
    };
}
