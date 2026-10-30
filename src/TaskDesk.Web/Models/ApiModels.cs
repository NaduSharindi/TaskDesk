using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using TaskDesk.Domain;

namespace TaskDesk.Models.Api;

// API contracts are plain classes (not records) so both System.Text.Json and XmlSerializer
// can (de)serialize them - the API supports JSON and XML via the Accept / Content-Type headers.

[XmlRoot("CreateTaskRequest")]
public sealed class ApiCreateTaskRequest
{
    /// <summary>The caller's own ID (e.g. helpdesk ticket "HD-2026-0042"). Makes retries idempotent.</summary>
    [Required, StringLength(64, MinimumLength = 1)]
    public string ExternalReference { get; set; } = string.Empty;

    [Required, StringLength(TaskItem.TitleMaxLength, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(TaskItem.DescriptionMaxLength)]
    public string? Description { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    /// <summary>yyyy-MM-dd</summary>
    [RegularExpression(@"^\d{4}-\d{2}-\d{2}$", ErrorMessage = "DueDate must use the format yyyy-MM-dd.")]
    public string? DueDate { get; set; }

    [Required, StringLength(10)]
    public string DepartmentCode { get; set; } = string.Empty;

    [EmailAddress, StringLength(256)]
    public string? AssigneeEmail { get; set; }
}

[XmlRoot("Comment")]
public sealed class ApiCommentRequest
{
    [Required, StringLength(TaskItem.CommentMaxLength, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

[XmlRoot("Task")]
public sealed class ApiTaskResponse
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    public string? ExternalReference { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public TaskPriority Priority { get; set; }

    public WorkStatus Status { get; set; }

    public string? DueDate { get; set; }

    public string DepartmentCode { get; set; } = string.Empty;

    public string? AssigneeName { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}

[XmlRoot("Tasks")]
public sealed class ApiTaskListResponse
{
    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    [XmlArray("Items"), XmlArrayItem("Task")]
    public List<ApiTaskResponse> Items { get; set; } = [];
}
