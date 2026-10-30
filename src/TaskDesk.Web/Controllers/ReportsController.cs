using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskDesk.Security;
using TaskDesk.Services;

namespace TaskDesk.Controllers;

[Authorize(Policy = Policies.ManagerOrAdmin)]
public sealed class ReportsController(ReportService reports, AuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Workload(CancellationToken cancellationToken) =>
        View(await reports.GetDepartmentWorkloadAsync(cancellationToken));

    [HttpGet]
    public async Task<IActionResult> ExportCsv(CancellationToken cancellationToken)
    {
        var bytes = await reports.ExportTasksCsvAsync(UserScope.From(User), cancellationToken);
        await audit.RecordAsync("ExportedTasksCsv", "Report", "tasks.csv", null, cancellationToken);
        return File(bytes, "text/csv", $"taskdesk-tasks-{DateTime.UtcNow:yyyyMMdd-HHmm}.csv");
    }
}
