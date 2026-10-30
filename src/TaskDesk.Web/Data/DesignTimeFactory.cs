using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TaskDesk.Common;

namespace TaskDesk.Data;

/// <summary>Used only by "dotnet ef migrations" (SQL Server). Override with TASKDESK_DESIGN_CONNECTION.</summary>
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("TASKDESK_DESIGN_CONNECTION")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=TaskDeskDb;Trusted_Connection=True;TrustServerCertificate=True";

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options;
        return new AppDbContext(options, new SystemUser());
    }

    private sealed class SystemUser : ICurrentUser
    {
        public string? UserId => null;

        public string UserName => "design-time";

        public string? IpAddress => null;

        public string? CorrelationId => null;
    }
}
