using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TaskDesk.Models;
using TaskDesk.Services;

namespace TaskDesk.Controllers;

public sealed class HomeController(TaskService tasks, ILogger<HomeController> logger) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken) =>
        View(await tasks.GetDashboardAsync(UserScope.From(User), cancellationToken));

    /// <summary>Friendly error page. Shows only a reference number - never stack traces.</summary>
    [AllowAnonymous, IgnoreAntiforgeryToken]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature is not null)
        {
            logger.LogError(feature.Error, "Unhandled exception on {Path} (reference {CorrelationId})",
                feature.Path, HttpContext.TraceIdentifier);
        }

        return View(new ErrorViewModel(HttpContext.TraceIdentifier, 500, "Something went wrong",
            "The error has been logged. If it keeps happening, contact the IT helpdesk and quote the reference below."));
    }

    [AllowAnonymous, IgnoreAntiforgeryToken]
    public IActionResult HttpStatus(int code)
    {
        var (title, message) = code switch
        {
            404 => ("Not found", "The page or task you are looking for does not exist, or you do not have access to it."),
            403 => ("Access denied", "Your role does not allow this action."),
            429 => ("Too many attempts", "Please wait a minute and try again."),
            _ => ("Request failed", "The request could not be completed."),
        };

        Response.StatusCode = code;
        return View("Error", new ErrorViewModel(HttpContext.TraceIdentifier, code, title, message));
    }
}
