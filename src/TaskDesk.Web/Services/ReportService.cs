using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;

namespace TaskDesk.Services;

public sealed class ReportService(AppDbContext db, TaskService taskService, IClock clock)
{
    /// <summary>
    /// Department workload. On SQL Server this runs the dbo.usp_GetDepartmentWorkload stored procedure
    /// (set-based, single round trip). Other providers (SQLite in tests) use the LINQ equivalent.
    /// </summary>
    public async Task<IReadOnlyList<WorkloadRow>> GetDepartmentWorkloadAsync(CancellationToken cancellationToken)
    {
        var today = clock.Today;

        if (db.Database.IsSqlServer())
        {
            var asOf = today.ToDateTime(TimeOnly.MinValue);
            return await db.Database
                .SqlQuery<WorkloadRow>($"EXEC dbo.usp_GetDepartmentWorkload @AsOfDate = {asOf}")
                .ToListAsync(cancellationToken);
        }

        var completedSince = clock.UtcNow.AddDays(-7);
        var departments = await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(cancellationToken);
        var tasks = await db.Tasks.AsNoTracking()
            .Select(t => new { t.DepartmentId, t.Status, t.DueDate, t.CompletedAtUtc })
            .ToListAsync(cancellationToken);

        return departments.Select(d =>
        {
            var mine = tasks.Where(t => t.DepartmentId == d.Id).ToList();
            var open = mine.Where(t => t.Status is WorkStatus.ToDo or WorkStatus.InProgress or WorkStatus.Blocked).ToList();
            return new WorkloadRow
            {
                DepartmentCode = d.Code,
                DepartmentName = d.Name,
                OpenTasks = open.Count,
                InProgressTasks = open.Count(t => t.Status == WorkStatus.InProgress),
                BlockedTasks = open.Count(t => t.Status == WorkStatus.Blocked),
                OverdueTasks = open.Count(t => t.DueDate < today),
                DueToday = open.Count(t => t.DueDate == today),
                CompletedLast7Days = mine.Count(t => t.Status == WorkStatus.Done && t.CompletedAtUtc >= completedSince),
            };
        }).ToList();
    }

    /// <summary>CSV export of the tasks the user can see (values escaped against formula injection).</summary>
    public async Task<byte[]> ExportTasksCsvAsync(UserScope user, CancellationToken cancellationToken)
    {
        var rows = await taskService.VisibleTo(user).AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new
            {
                t.Id,
                t.Title,
                t.Priority,
                t.Status,
                t.DueDate,
                Department = t.Department!.Code,
                Assignee = t.AssignedTo != null ? t.AssignedTo.FullName : null,
                t.CreatedByName,
                t.CreatedAtUtc,
                t.CompletedAtUtc,
            })
            .ToListAsync(cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("Reference,Title,Priority,Status,DueDate,Department,AssignedTo,CreatedBy,CreatedAt,CompletedAt");
        foreach (var r in rows)
        {
            csv.AppendJoin(',',
                    TaskItem.FormatReference(r.Id),
                    Csv.Escape(r.Title),
                    r.Priority,
                    r.Status,
                    r.DueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    r.Department,
                    Csv.Escape(r.Assignee),
                    Csv.Escape(r.CreatedByName),
                    clock.ToOfficeTime(r.CreatedAtUtc).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                    r.CompletedAtUtc is { } done ? clock.ToOfficeTime(done).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty)
                .AppendLine();
        }

        // UTF-8 BOM so Excel opens Sinhala/Tamil names correctly.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }
}
