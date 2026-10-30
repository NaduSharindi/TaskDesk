using System.Globalization;
using System.Security.Claims;
using TaskDesk.Domain;

namespace TaskDesk.Services;

/// <summary>The signed-in user's identity and permissions, read from the auth cookie claims.</summary>
public sealed record UserScope(string UserId, string UserName, string FullName, int DepartmentId, bool IsAdmin, bool IsManager)
{
    public static UserScope From(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("The current user has no identifier claim.");

        _ = int.TryParse(principal.FindFirst(AppClaimTypes.DepartmentId)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var departmentId);

        return new UserScope(
            userId,
            principal.Identity?.Name ?? userId,
            principal.FindFirst(AppClaimTypes.FullName)?.Value ?? principal.Identity?.Name ?? userId,
            departmentId,
            principal.IsInRole(Roles.Admin),
            principal.IsInRole(Roles.Manager));
    }
}

public sealed class TaskFilter
{
    public string? Search { get; set; }

    public WorkStatus? Status { get; set; }

    public TaskPriority? Priority { get; set; }

    /// <summary>"all" (everything I may see), "assigned" (assigned to me), "created" (created by me).</summary>
    public string View { get; set; } = "assigned";

    public bool OverdueOnly { get; set; }

    public bool IncludeClosed { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed record TaskInput(string Title, string? Description, TaskPriority Priority, DateOnly? DueDate, string? AssignedToId);

public sealed record TaskListItem(
    int Id,
    string Title,
    TaskPriority Priority,
    WorkStatus Status,
    DateOnly? DueDate,
    string? AssigneeName,
    string DepartmentName,
    bool IsOverdue)
{
    public string Reference => TaskItem.FormatReference(Id);
}

public sealed record TaskDetailsResult(
    TaskItem Task,
    bool CanEdit,
    bool CanChangeStatus,
    IReadOnlyList<AuditEntry> History,
    IReadOnlyList<string> DueDateWarnings);

public sealed record SaveResult(int TaskId, IReadOnlyList<string> Warnings);

public sealed record UserOption(string Id, string Name);

public sealed record Dashboard(
    int MyOpen,
    int Overdue,
    int DueToday,
    int DueNext7Days,
    IReadOnlyDictionary<WorkStatus, int> ByStatus,
    IReadOnlyList<TaskListItem> MyUpcoming,
    bool IsTeamView);

/// <summary>Row returned by dbo.usp_GetDepartmentWorkload (column names must match).</summary>
public sealed class WorkloadRow
{
    public string DepartmentCode { get; set; } = string.Empty;

    public string DepartmentName { get; set; } = string.Empty;

    public int OpenTasks { get; set; }

    public int InProgressTasks { get; set; }

    public int BlockedTasks { get; set; }

    public int OverdueTasks { get; set; }

    public int DueToday { get; set; }

    public int CompletedLast7Days { get; set; }
}
