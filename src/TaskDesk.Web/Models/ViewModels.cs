using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Microsoft.AspNetCore.Mvc.Rendering;
using TaskDesk.Common;
using TaskDesk.Domain;
using TaskDesk.Services;

namespace TaskDesk.Models;

public sealed class LoginViewModel
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), StringLength(128)]
    public string Password { get; set; } = string.Empty;

    public string? ReturnUrl { get; set; }
}

public sealed class TaskFormViewModel
{
    public int? Id { get; set; }

    [Required, StringLength(TaskItem.TitleMaxLength, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [StringLength(TaskItem.DescriptionMaxLength)]
    public string? Description { get; set; }

    [Required]
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;

    [Display(Name = "Due date")]
    public DateOnly? DueDate { get; set; }

    [Display(Name = "Assign to")]
    public string? AssignedToId { get; set; }

    public Guid ConcurrencyStamp { get; set; }

    [BindNever, ValidateNever]
    public IReadOnlyList<SelectListItem> AssigneeOptions { get; set; } = [];

    public TaskInput ToInput() => new(Title, Description, Priority, DueDate, AssignedToId);

    public static TaskFormViewModel From(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Priority = task.Priority,
        DueDate = task.DueDate,
        AssignedToId = task.AssignedToId,
        ConcurrencyStamp = task.ConcurrencyStamp,
    };
}

public sealed class CommentInputModel
{
    [Required, StringLength(TaskItem.CommentMaxLength, MinimumLength = 1)]
    public string Text { get; set; } = string.Empty;
}

public sealed class TaskListViewModel
{
    public required TaskFilter Filter { get; init; }

    public required PagedResult<TaskListItem> Result { get; init; }

    public bool CanSeeTeam { get; init; }
}

public sealed class TaskDetailsViewModel
{
    public required TaskDetailsResult Details { get; init; }

    public CommentInputModel NewComment { get; init; } = new();
}

public sealed class AuditListViewModel
{
    public required PagedResult<AuditEntry> Result { get; init; }

    public string? UserName { get; init; }

    public string? EventType { get; init; }
}

public sealed record ErrorViewModel(string? CorrelationId, int StatusCode, string Title, string Message);
