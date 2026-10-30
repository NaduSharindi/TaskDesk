using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;
using TaskDesk.Infrastructure;
using TaskDesk.Integration;
using TaskDesk.Security;
using TaskDesk.Services;

var builder = WebApplication.CreateBuilder(args);
var isLocal = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// ---------- Configuration (validated at startup) ----------
builder.Services.AddOptions<OfficeOptions>().BindConfiguration(OfficeOptions.SectionName);
builder.Services.AddOptions<MonitoringOptions>().BindConfiguration(MonitoringOptions.SectionName);
builder.Services.AddOptions<HolidayServiceOptions>().BindConfiguration(HolidayServiceOptions.SectionName);
builder.Services.AddOptions<IntegrationOptions>().BindConfiguration(IntegrationOptions.SectionName);

// ---------- Data ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();
builder.Services.AddSingleton<IClock, OfficeClock>();
builder.Services.AddDbContext<AppDbContext>((services, options) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var connectionString = configuration.GetConnectionString("TaskDesk");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException("Connection string 'TaskDesk' is not configured. See docs/DEPLOYMENT-IIS.md.");
    }

    if (string.Equals(configuration["Database:Provider"], "Sqlite", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(connectionString, sql => sql.CommandTimeout(30));
    }
});

// ---------- Identity & security ----------
builder.Services
    .AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.AllowedForNewUsers = true;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "TaskDesk.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = isLocal ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddAuthentication()
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(Policies.ManagerOrAdmin, p => p.RequireRole(Roles.Admin, Roles.Manager))
    .AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin))
    .AddPolicy(Policies.IntegrationClient, p => p
        .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
        .RequireAuthenticatedUser()
        .RequireRole(Roles.IntegrationClient))
    // Secure by default: every page needs a signed-in user unless marked [AllowAnonymous].
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>()
                    .GetValue("Security:LoginAttemptsPerMinute", 10),
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services.Configure<FormOptions>(o => o.ValueLengthLimit = 16 * 1024);

// ---------- Application ----------
builder.Services.AddScoped<TaskService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<IntegrationTaskService>();
builder.Services.AddScoped<DbInitializer>();

// ---------- Integration: SOAP holiday service ----------
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IHolidayCalendar, SoapHolidayCalendarClient>((services, client) =>
{
    var options = services.GetRequiredService<IOptions<HolidayServiceOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});

// ---------- Web ----------
builder.Services
    .AddControllersWithViews(options =>
    {
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()); // CSRF protection on every POST
        options.Filters.Add<DomainExceptionFilter>();
    })
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddXmlSerializerFormatters();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database", tags: ["ready"]);

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestTimingMiddleware>();
app.UseExceptionHandler();
if (!isLocal)
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/HttpStatus", "?code={0}");
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
}).AllowAnonymous();

if (isLocal)
{
    app.MapMockHolidaySoapService();
}

await app.InitialiseDatabaseAsync();
await app.RunAsync();

/// <summary>Entry point marker for WebApplicationFactory integration tests.</summary>
public partial class Program
{
}
