using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskDesk.Domain;

namespace TaskDesk.Infrastructure;

/// <summary>API (/api/*) errors -> problem+json. Other paths fall through to the /Home/Error page.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Not found"),
            ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ConcurrencyException => (StatusCodes.Status409Conflict, "Conflict"),
            BusinessRuleException => (StatusCodes.Status422UnprocessableEntity, "Business rule violation"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled API exception");
        }

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred. Quote the correlationId when contacting support."
                : exception.Message,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["correlationId"] = httpContext.TraceIdentifier;

        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}

/// <summary>MVC: NotFound / Forbidden exceptions become proper 404 / 403 pages instead of 500s.</summary>
public sealed class DomainExceptionFilter(ILogger<DomainExceptionFilter> logger) : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
        {
            return; // handled by ApiExceptionHandler
        }

        switch (context.Exception)
        {
            case NotFoundException:
                context.Result = new NotFoundResult();
                context.ExceptionHandled = true;
                break;
            case ForbiddenException ex:
                logger.LogWarning("Forbidden: {Message} (user {User})", ex.Message, context.HttpContext.User.Identity?.Name);
                context.Result = new ForbidResult();
                context.ExceptionHandled = true;
                break;
        }
    }
}