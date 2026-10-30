using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;
using TaskDesk.Models.Api;

namespace TaskDesk.Integration;

/// <summary>
/// Use cases for external systems (e.g. the IT Helpdesk) calling the REST/XML API.
/// Least privilege: a client can only read and comment on the tasks it created itself.
/// </summary>
public sealed class IntegrationTaskService(AppDbContext db, IClock clock, ILogger<IntegrationTaskService> logger)
{
    public static string SourceFor(string clientName) => "API:" + clientName;

    public async Task<(ApiTaskResponse Task, bool Created)> CreateAsync(
        string clientName, ApiCreateTaskRequest request, CancellationToken cancellationToken)
    {
        var source = SourceFor(clientName);
        var externalReference = request.ExternalReference.Trim();

        var existing = await FindByReferenceAsync(source, externalReference, cancellationToken);
        if (existing is not null)
        {
            return (existing, false); // idempotent retry
        }

        var department = await db.Departments
            .FirstOrDefaultAsync(d => d.Code == request.DepartmentCode.Trim().ToUpper(), cancellationToken)
            ?? throw new BusinessRuleException($"Unknown department code '{request.DepartmentCode}'.");

        string? assigneeId = null;
        if (!string.IsNullOrWhiteSpace(request.AssigneeEmail))
        {
            var normalizedEmail = request.AssigneeEmail.Trim().ToUpperInvariant();
            var assignee = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail && u.IsActive, cancellationToken);
            if (assignee is null || assignee.DepartmentId != department.Id)
            {
                throw new BusinessRuleException("The assignee must be an active user of the given department.");
            }

            assigneeId = assignee.Id;
        }

        DateOnly? dueDate = null;
        if (!string.IsNullOrWhiteSpace(request.DueDate))
        {
            if (!DateOnly.TryParseExact(request.DueDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                throw new BusinessRuleException("DueDate must use the format yyyy-MM-dd.");
            }

            dueDate = parsed;
        }

        var task = new TaskItem(
            request.Title,
            request.Description,
            request.Priority,
            dueDate,
            department.Id,
            assigneeId,
            createdById: null,
            createdByName: clientName,
            source,
            externalReference,
            clock.UtcNow);

        db.Tasks.Add(task);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Two identical requests raced; the unique index on (Source, ExternalReference) let only one in.
            db.ChangeTracker.Clear();
            var winner = await FindByReferenceAsync(source, externalReference, cancellationToken);
            if (winner is null)
            {
                throw;
            }

            return (winner, false);
        }

        logger.LogInformation("Task {Reference} created via API by {Client} (external ref {ExternalReference})",
            task.Reference, clientName, externalReference);

        return ((await GetAsync(clientName, task.Id, cancellationToken))!, true);
    }

    public async Task<ApiTaskListResponse> ListAsync(
        string clientName, WorkStatus? status, DateTime? updatedSinceUtc, int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = ClientTasks(clientName);

        if (status is { } s)
        {
            query = query.Where(t => t.Status == s);
        }

        if (updatedSinceUtc is { } since)
        {
            var sinceUtc = since.ToUniversalTime();
            query = query.Where(t => t.UpdatedAtUtc >= sinceUtc);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await Project(query.OrderBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize))
            .ToListAsync(cancellationToken);

        return new ApiTaskListResponse { Page = page, PageSize = pageSize, TotalCount = total, Items = items };
    }

    public Task<ApiTaskResponse?> GetAsync(string clientName, int id, CancellationToken cancellationToken) =>
        Project(ClientTasks(clientName).Where(t => t.Id == id)).FirstOrDefaultAsync(cancellationToken);

    public Task<ApiTaskResponse?> GetByReferenceAsync(string clientName, string externalReference, CancellationToken cancellationToken) =>
        FindByReferenceAsync(SourceFor(clientName), externalReference.Trim(), cancellationToken);

    public async Task AddCommentAsync(string clientName, int id, string text, CancellationToken cancellationToken)
    {
        var task = await ClientTasks(clientName).AsTracking().FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
            ?? throw new NotFoundException("Task", TaskItem.FormatReference(id));

        task.AddComment(null, clientName, text, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
    }

    private IQueryable<TaskItem> ClientTasks(string clientName)
    {
        var source = SourceFor(clientName);
        return db.Tasks.AsNoTracking().Where(t => t.Source == source);
    }

    private Task<ApiTaskResponse?> FindByReferenceAsync(string source, string externalReference, CancellationToken cancellationToken) =>
        Project(db.Tasks.AsNoTracking().Where(t => t.Source == source && t.ExternalReference == externalReference))
            .FirstOrDefaultAsync(cancellationToken);

    private static IQueryable<ApiTaskResponse> Project(IQueryable<TaskItem> query) =>
        query.Select(t => new ApiTaskResponse
        {
            Id = t.Id,
            Reference = "TD-" + t.Id.ToString().PadLeft(5, '0'),
            ExternalReference = t.ExternalReference,
            Title = t.Title,
            Description = t.Description,
            Priority = t.Priority,
            Status = t.Status,
            DueDate = t.DueDate.HasValue ? t.DueDate.Value.ToString("yyyy-MM-dd") : null,
            DepartmentCode = t.Department!.Code,
            AssigneeName = t.AssignedTo != null ? t.AssignedTo.FullName : null,
            CreatedAtUtc = t.CreatedAtUtc,
            UpdatedAtUtc = t.UpdatedAtUtc,
            CompletedAtUtc = t.CompletedAtUtc,
        });
}
