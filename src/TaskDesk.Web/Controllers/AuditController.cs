using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskDesk.Models;
using TaskDesk.Security;
using TaskDesk.Services;

namespace TaskDesk.Controllers;

[Authorize(Policy = Policies.AdminOnly)]
public sealed class AuditController(AuditService audit) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string? userName, string? eventType, int page = 1, CancellationToken cancellationToken = default)
    {
        var result = await audit.SearchAsync(userName, eventType, page, 50, cancellationToken);
        return View(new AuditListViewModel { Result = result, UserName = userName, EventType = eventType });
    }
}
