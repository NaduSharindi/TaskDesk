using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskDesk.Domain;
using TaskDesk.Integration;
using TaskDesk.Models.Api;
using TaskDesk.Security;

namespace TaskDesk.Controllers.Api;

/// <summary>
/// REST API for system integration (e.g. the IT Helpdesk raising tasks automatically).
/// Auth: X-Api-Key header. Formats: JSON (default) or XML (Accept / Content-Type: application/xml).
/// </summary>
[ApiController]
[Route("api/v1/tasks")]
[Authorize(Policy = Policies.IntegrationClient)]
[IgnoreAntiforgeryToken] // machine clients authenticate with a header, not a cookie, so CSRF does not apply
[Produces("application/json", "application/xml")]
public sealed class TasksApiController(IntegrationTaskService integration) : ControllerBase
{
    private string ClientName => User.Identity?.Name ?? "unknown";

    /// <summary>Lists tasks created by this client. Use updatedSinceUtc for incremental polling.</summary>
    [HttpGet]
    [ProducesResponseType<ApiTaskListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ApiTaskListResponse>> List(
        [FromQuery] WorkStatus? status,
        [FromQuery] DateTime? updatedSinceUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        Ok(await integration.ListAsync(ClientName, status, updatedSinceUtc, page, pageSize, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType<ApiTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiTaskResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var task = await integration.GetAsync(ClientName, id, cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    [HttpGet("by-reference/{externalReference}")]
    [ProducesResponseType<ApiTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiTaskResponse>> GetByReference(string externalReference, CancellationToken cancellationToken)
    {
        var task = await integration.GetByReferenceAsync(ClientName, externalReference, cancellationToken);
        return task is null ? NotFound() : Ok(task);
    }

    /// <summary>Creates a task. Idempotent on ExternalReference: a retry returns 200 with the existing task.</summary>
    [HttpPost]
    [Consumes("application/json", "application/xml")]
    [ProducesResponseType<ApiTaskResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiTaskResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ApiTaskResponse>> Create(ApiCreateTaskRequest request, CancellationToken cancellationToken)
    {
        var (task, created) = await integration.CreateAsync(ClientName, request, cancellationToken);
        return created ? CreatedAtAction(nameof(Get), new { id = task.Id }, task) : Ok(task);
    }

    [HttpPost("{id:int}/comments")]
    [Consumes("application/json", "application/xml")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddComment(int id, ApiCommentRequest request, CancellationToken cancellationToken)
    {
        await integration.AddCommentAsync(ClientName, id, request.Text, cancellationToken);
        return NoContent();
    }
}
