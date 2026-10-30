using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace TaskDesk.Tests.Integration;

/// <summary>Runs the real app (middleware, Identity, MVC, API) in memory against a temporary SQLite file.</summary>
public sealed class TaskDeskWebFactory : WebApplicationFactory<Program>
{
    public const string Password = "Test@Pass1234!";
    public const string ApiKey = "test-helpdesk-api-key";

    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"taskdesk-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:TaskDesk"] = $"Data Source={_databasePath}",
            ["Database:Provider"] = "Sqlite",
            ["Seed:Enabled"] = "true",
            ["Seed:DefaultPassword"] = Password,
            ["Security:LoginAttemptsPerMinute"] = "1000",
            ["Integration:HolidayService:Url"] = "",
            ["Integration:Clients:0:Name"] = "HelpdeskSystem",
            ["Integration:Clients:0:ApiKeySha256"] = "7b0da4dd4d29bf95f7dd80c10b1e959da1826f64714c0a5d2c76cf14827ad586",
            ["Integration:Clients:0:Enabled"] = "true",
        }));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        try
        {
            File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // best effort
        }
    }
}
