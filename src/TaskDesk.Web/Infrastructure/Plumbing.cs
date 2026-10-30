using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TaskDesk.Common;
using TaskDesk.Data;

namespace TaskDesk.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public string? UserId => accessor.HttpContext?.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    public string UserName =>
        accessor.HttpContext?.User.Identity is { IsAuthenticated: true, Name: { } name } ? name : "system";

    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId => accessor.HttpContext?.TraceIdentifier;
}

/// <summary>Health check JSON for monitoring tools (SCOM, Zabbix, uptime monitors, load balancers).</summary>
public static class HealthResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds),
            version = typeof(HealthResponseWriter).Assembly.GetName().Version?.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                durationMs = Math.Round(e.Value.Duration.TotalMilliseconds),
                error = e.Value.Exception?.Message,
            }),
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}

public static class DatabaseStartup
{
    public static async Task InitialiseDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue("Database:InitializeOnStartup", true))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
        await initializer.InitialiseAsync();

        if (app.Configuration.GetValue("Seed:Enabled", false))
        {
            await initializer.SeedAsync();
        }
    }
}
