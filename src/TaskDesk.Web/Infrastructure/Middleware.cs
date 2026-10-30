using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using TaskDesk.Common;

namespace TaskDesk.Infrastructure;

/// <summary>
/// Gives every request a correlation ID (X-Correlation-ID). It is returned to the caller, shown on
/// error pages ("quote this reference to support"), written to every log line and audit row.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = IsSafe(incoming) ? incoming : Guid.NewGuid().ToString("N")[..16];

        context.Items[ItemKey] = correlationId;
        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static bool IsSafe(string value) =>
        value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

/// <summary>Logs slow requests (performance monitoring) and optionally emits a Server-Timing header.</summary>
public sealed class RequestTimingMiddleware(
    RequestDelegate next,
    ILogger<RequestTimingMiddleware> logger,
    IOptions<MonitoringOptions> options)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var started = Stopwatch.GetTimestamp();

        if (options.Value.EmitServerTimingHeader)
        {
            context.Response.OnStarting(() =>
            {
                var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                context.Response.Headers["Server-Timing"] = string.Create(CultureInfo.InvariantCulture, $"app;dur={elapsedMs:F0}");
                return Task.CompletedTask;
            });
        }

        await next(context);

        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        if (elapsed >= options.Value.SlowRequestThresholdMs)
        {
            logger.LogWarning(
                "Slow request: {Method} {Path} returned {StatusCode} in {ElapsedMs:F0} ms",
                context.Request.Method, context.Request.Path, context.Response.StatusCode, elapsed);
        }
    }
}

/// <summary>OWASP secure headers. The UI uses no inline script, so a strict CSP is possible.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "font-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            return Task.CompletedTask;
        });

        return next(context);
    }
}
