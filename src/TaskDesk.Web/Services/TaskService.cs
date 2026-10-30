using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;
using TaskDesk.Integration;

namespace TaskDesk.Services;

/// <summary>
/// Use cases for the web UI. Access rules:
///  * Admin   - sees and manages every task
///  * Manager - sees and manages tasks of their department (plus their own)
///  * Staff   - sees tasks they created or that are assigned to them; can only assign to themselves
/// </summary>
public sealed class TaskService(
    AppDbContext db,
    IClock clock,
    IHolidayCalendar holidayCalendar,
    ILogger<TaskService> logger)
{
    public IQueryable<TaskItem> VisibleTo(UserScope user)
    {
        var query = db.Tasks.AsQueryable();

        if (user.IsAdmin)
        {
            return query;
        }

        if (user.IsManager)
        {
            return query.Where(t =>
                t.DepartmentId == user.DepartmentId || t.CreatedById == user.UserId || t.AssignedToId == user.UserId);
        }

        return query.Where(t => t.CreatedById == user.UserId || t.AssignedToId == user.UserId);
    }

    public async Task<PagedResult<TaskListItem>> SearchAsync(UserScope user, TaskFilter filter, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(filter.Page, filter.PageSize);
        var today = clock.Today;
        var query = VisibleTo(user).AsNoTracking();

        query = filter.View switch
        {
            "created" => query.Where(t => t.CreatedById == user.UserId),
            "all" => query,
            _ => query.Where(t => t.AssignedToId == user.UserId),
        };

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            var referenceId = TryParseReference(term);
            query = referenceId is { } id
                ? query.Where(t => t.Id == id)
                : query.Where(t => t.Title.Contains(term) || (t.Description != null && t.Description.Contains(term)));
        }

        if (filter.Status is { } status)
        {
            query = query.Where(t => t.Status == status);
        }
        else if (!filter.IncludeClosed)
        {
            query = query.Where(t => t.Status != WorkStatus.Done && t.Status != WorkStatus.Cancelled);
        }

        if (filter.Priority is { } priority)
        {
            query = query.Where(t => t.Priority == priority);
        }

        if (filter.OverdueOnly)
        {
            query = query.Where(t => t.DueDate < today && t.Status != WorkStatus.Done && t.Status != WorkStatus.Cancelled);
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderBy(t => t.Status == WorkStatus.Done || t.Status == WorkStatus.Cancelled ? 1 : 0)
            .ThenBy(t => t.DueDate == null ? 1 : 0)
            .ThenBy(t => t.DueDate)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Priority,
                t.Status,
                t.DueDate,
                AssigneeName = t.AssignedTo != null ? t.AssignedTo.FullName : null,
                DepartmentName = t.Department!.Name,
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new TaskListItem(
                r.Id, r.Title, r.Priority, r.Status, r.DueDate, r.AssigneeName, r.DepartmentName,
                IsOverdue: r.DueDate < today && r.Status is not (WorkStatus.Done or WorkStatus.Cancelled)))
            .ToList();

        return new PagedResult<TaskListItem>(items, page, pageSize, total);
    }

    public async Task<TaskDetailsResult> GetDetailsAsync(UserScope user, int id, CancellationToken cancellationToken)
    {
        var task = await VisibleTo(user)
            .AsNoTracking()
            .Include(t => t.Department)
            .Include(t => t.AssignedTo)
            .Include(t => t.Comments)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        var entityId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var history = await db.AuditEntries
            .AsNoTracking()
            .Where(a => a.EntityName == nameof(TaskItem) && a.EntityId == entityId)
            .OrderByDescending(a => a.Id)
            .Take(50)
            .ToListAsync(cancellationToken);

        return new TaskDetailsResult(
            task,
            CanEdit(user, task),
            CanChangeStatus(user, task),
            history,
            task.IsOpen ? await GetDueDateWarningsAsync(task.DueDate, cancellationToken) : []);
    }

    public async Task<TaskItem> GetForEditAsync(UserScope user, int id, CancellationToken cancellationToken)
    {
        var task = await VisibleTo(user).AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        if (!CanEdit(user, task))
        {
            throw new ForbiddenException("You are not allowed to edit this task.");
        }

        return task;
    }

    public async Task<SaveResult> CreateAsync(UserScope user, TaskInput input, CancellationToken cancellationToken)
    {
        if (input.DueDate is { } due && due < clock.Today)
        {
            throw new BusinessRuleException("Due date cannot be in the past.");
        }

        var assignee = await ResolveAssigneeAsync(user, input.AssignedToId ?? user.UserId, cancellationToken);
        var task = new TaskItem(
            input.Title,
            input.Description,
            input.Priority,
            input.DueDate,
            assignee.DepartmentId,
            assignee.Id,
            user.UserId,
            user.FullName,
            TaskItem.WebSource,
            externalReference: null,
            clock.UtcNow);

        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Task {Reference} created by {User}, assigned to {AssigneeId}", task.Reference, user.UserName, assignee.Id);
        return new SaveResult(task.Id, await GetDueDateWarningsAsync(task.DueDate, cancellationToken));
    }

    public async Task<SaveResult> UpdateAsync(
        UserScope user, int id, TaskInput input, Guid concurrencyStamp, CancellationToken cancellationToken)
    {
        var task = await VisibleTo(user).FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        if (!CanEdit(user, task))
        {
            throw new ForbiddenException("You are not allowed to edit this task.");
        }

        // The stamp the user saw when the form was opened. If someone saved in between, SaveChanges fails.
        db.Entry(task).Property(t => t.ConcurrencyStamp).OriginalValue = concurrencyStamp;

        if (input.DueDate is { } due && due != task.DueDate && due < clock.Today)
        {
            throw new BusinessRuleException("Due date cannot be moved into the past.");
        }

        var now = clock.UtcNow;
        task.UpdateDetails(input.Title, input.Description, input.Priority, input.DueDate, now);

        var newAssigneeId = input.AssignedToId ?? task.AssignedToId ?? user.UserId;
        if (newAssigneeId != task.AssignedToId)
        {
            var assignee = await ResolveAssigneeAsync(user, newAssigneeId, cancellationToken);
            task.AssignTo(assignee.Id, assignee.DepartmentId, now);
        }

        await SaveWithConcurrencyCheckAsync(cancellationToken);
        return new SaveResult(task.Id, await GetDueDateWarningsAsync(task.DueDate, cancellationToken));
    }

    public async Task ChangeStatusAsync(UserScope user, int id, WorkStatus status, CancellationToken cancellationToken)
    {
        var task = await VisibleTo(user).FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        if (!CanChangeStatus(user, task))
        {
            throw new ForbiddenException("You are not allowed to change the status of this task.");
        }

        task.ChangeStatus(status, clock.UtcNow);
        await SaveWithConcurrencyCheckAsync(cancellationToken);
        logger.LogInformation("Task {Reference} moved to {Status} by {User}", task.Reference, status, user.UserName);
    }

    public async Task AddCommentAsync(UserScope user, int id, string text, CancellationToken cancellationToken)
    {
        var task = await VisibleTo(user).FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        task.AddComment(user.UserId, user.FullName, text, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>People the current user may assign work to.</summary>
    public async Task<IReadOnlyList<UserOption>> GetAssigneeOptionsAsync(UserScope user, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking().Where(u => u.IsActive);

        if (!user.IsAdmin)
        {
            query = user.IsManager
                ? query.Where(u => u.DepartmentId == user.DepartmentId)
                : query.Where(u => u.Id == user.UserId);
        }

        return await query
            .OrderBy(u => u.FullName)
            .Select(u => new UserOption(u.Id, u.FullName + " (" + u.Department!.Code + ")"))
            .ToListAsync(cancellationToken);
    }

    public async Task<Dashboard> GetDashboardAsync(UserScope user, CancellationToken cancellationToken)
    {
        var today = clock.Today;
        var nextWeek = today.AddDays(7);
        var visibleOpen = VisibleTo(user).AsNoTracking()
            .Where(t => t.Status != WorkStatus.Done && t.Status != WorkStatus.Cancelled);

        var myOpen = await visibleOpen.CountAsync(t => t.AssignedToId == user.UserId, cancellationToken);
        var overdue = await visibleOpen.CountAsync(t => t.DueDate < today, cancellationToken);
        var dueToday = await visibleOpen.CountAsync(t => t.DueDate == today, cancellationToken);
        var dueWeek = await visibleOpen.CountAsync(t => t.DueDate >= today && t.DueDate <= nextWeek, cancellationToken);

        var byStatus = await VisibleTo(user).AsNoTracking()
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var upcoming = await SearchAsync(user, new TaskFilter { View = "assigned", PageSize = 5 }, cancellationToken);

        return new Dashboard(
            myOpen,
            overdue,
            dueToday,
            dueWeek,
            Enum.GetValues<WorkStatus>().ToDictionary(s => s, s => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0),
            upcoming.Items,
            user.IsAdmin || user.IsManager);
    }

    /// <summary>Non-blocking warnings: weekend or public holiday (from the SOAP holiday service).</summary>
    public async Task<IReadOnlyList<string>> GetDueDateWarningsAsync(DateOnly? dueDate, CancellationToken cancellationToken)
    {
        if (dueDate is not { } due)
        {
            return [];
        }

        var warnings = new List<string>();
        if (due.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            warnings.Add($"The due date {due:dd MMM yyyy} is a {due.DayOfWeek}.");
        }

        var holidays = await holidayCalendar.GetHolidaysAsync(due.Year, cancellationToken);
        var holiday = holidays.FirstOrDefault(h => h.Date == due);
        if (holiday is not null)
        {
            warnings.Add($"The due date {due:dd MMM yyyy} is a public holiday ({holiday.Name}).");
        }

        return warnings;
    }

    public static bool CanEdit(UserScope user, TaskItem task) =>
        task.IsOpen
        && (user.IsAdmin
            || (user.IsManager && task.DepartmentId == user.DepartmentId)
            || task.CreatedById == user.UserId);

    public static bool CanChangeStatus(UserScope user, TaskItem task) =>
        user.IsAdmin
        || (user.IsManager && task.DepartmentId == user.DepartmentId)
        || task.CreatedById == user.UserId
        || task.AssignedToId == user.UserId;

    private async Task<AppUser> ResolveAssigneeAsync(UserScope user, string assigneeId, CancellationToken cancellationToken)
    {
        if (!user.IsAdmin && !user.IsManager && assigneeId != user.UserId)
        {
            throw new ForbiddenException("Staff members can only assign tasks to themselves.");
        }

        var assignee = await db.Users.FirstOrDefaultAsync(u => u.Id == assigneeId && u.IsActive, cancellationToken)
            ?? throw new BusinessRuleException("The selected assignee does not exist or is inactive.");

        if (user.IsManager && !user.IsAdmin && assignee.DepartmentId != user.DepartmentId)
        {
            throw new ForbiddenException("Managers can only assign tasks within their own department.");
        }

        return assignee;
    }

    private async Task SaveWithConcurrencyCheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyException();
        }
    }

    private static int? TryParseReference(string term)
    {
        var digits = term.StartsWith("TD-", StringComparison.OrdinalIgnoreCase) ? term[3..] : null;
        return digits is not null && int.TryParse(digits, out var id) ? id : null;
    }
}
