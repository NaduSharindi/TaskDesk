using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaskDesk.Domain;

namespace TaskDesk.Security;

public static class Policies
{
    public const string ManagerOrAdmin = nameof(ManagerOrAdmin);
    public const string AdminOnly = nameof(AdminOnly);
    public const string IntegrationClient = nameof(IntegrationClient);
}

public static class RateLimitPolicies
{
    public const string Login = "login";
}

/// <summary>Adds department and display name to the auth cookie so authorization needs no extra DB call.</summary>
public sealed class AppClaimsPrincipalFactory(
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<AppUser, IdentityRole>(userManager, roleManager, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(AppClaimTypes.DepartmentId, user.DepartmentId.ToString(CultureInfo.InvariantCulture)));
        identity.AddClaim(new Claim(AppClaimTypes.FullName, user.FullName));
        return identity;
    }
}

public sealed class IntegrationOptions
{
    public const string SectionName = "Integration";

    public List<IntegrationClient> Clients { get; set; } = [];
}

public sealed class IntegrationClient
{
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex) of the API key. The plain key is never stored in configuration.</summary>
    public string ApiKeySha256 { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
}

/// <summary>
/// "X-Api-Key" authentication for system-to-system calls. Keys are compared as SHA-256 hashes in
/// constant time; failures return RFC 9457 problem+json instead of redirecting to the login page.
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptionsMonitor<IntegrationOptions> integration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    public static string HashKey(string apiKey) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var provided = Request.Headers[HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(provided))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        foreach (var client in integration.CurrentValue.Clients.Where(c => c.Enabled && c.ApiKeySha256.Length == 64))
        {
            byte[] expected;
            try
            {
                expected = Convert.FromHexString(client.ApiKeySha256);
            }
            catch (FormatException)
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(providedHash, expected))
            {
                var identity = new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, client.Name), new Claim(ClaimTypes.Role, Roles.IntegrationClient)],
                    SchemeName);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
            }
        }

        Logger.LogWarning("Rejected API call with an invalid API key from {IpAddress}", Context.Connection.RemoteIpAddress);
        return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = $"{SchemeName} header=\"{HeaderName}\"";
        return WriteProblemAsync(StatusCodes.Status401Unauthorized, "Unauthorized", $"A valid {HeaderName} header is required.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(StatusCodes.Status403Forbidden, "Forbidden", "This API client is not allowed to perform the operation.");

    private Task WriteProblemAsync(int status, string title, string detail)
    {
        Response.StatusCode = status;
        return Response.WriteAsJsonAsync(
            new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = Request.Path },
            options: null,
            contentType: "application/problem+json");
    }
}
