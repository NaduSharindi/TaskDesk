using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Domain;

namespace TaskDesk.Data;

public sealed partial class DbInitializer(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IClock clock,
    IConfiguration configuration,
    ILogger<DbInitializer> logger)
{
    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();

    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        if (db.Database.IsSqlServer() && db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        if (db.Database.IsSqlServer())
        {
            await DeploySqlObjectsAsync(cancellationToken);
        }
    }

    /// <summary>Deploys the stored procedures in /database (embedded) - idempotent via CREATE OR ALTER.</summary>
    private async Task DeploySqlObjectsAsync(CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("TaskDesk.Sql.", StringComparison.Ordinal)).Order())
        {
            await using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var script = await reader.ReadToEndAsync(cancellationToken);

            foreach (var batch in GoSeparator().Split(script).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await db.Database.ExecuteSqlRawAsync(batch, cancellationToken);
            }

            logger.LogInformation("Deployed SQL script {Script}", name);
        }
    }

    /// <summary>Demo data for local development and UAT environments only.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var role in Roles.UserRoles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var password = configuration["Seed:DefaultPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:DefaultPassword not set - skipping demo users");
            return;
        }

        var it = new Department("IT", "Information Technology");
        var finance = new Department("FIN", "Finance");
        var hr = new Department("HR", "Human Resources");
        var operations = new Department("OPS", "Branch Operations");
        db.Departments.AddRange(it, finance, hr, operations);
        await db.SaveChangesAsync(cancellationToken);

        var admin = await CreateUserAsync("admin@taskdesk.local", "System Administrator", it.Id, Roles.Admin, password);
        var manager = await CreateUserAsync("manager.it@taskdesk.local", "Dilani Jayasinghe", it.Id, Roles.Manager, password);
        var staffIt = await CreateUserAsync("staff.it@taskdesk.local", "Ruwan Bandara", it.Id, Roles.Staff, password);
        var staffFin = await CreateUserAsync("staff.fin@taskdesk.local", "Sachini Perera", finance.Id, Roles.Staff, password);
        await CreateUserAsync("manager.fin@taskdesk.local", "Mahesh Gunawardena", finance.Id, Roles.Manager, password);

        var now = clock.UtcNow;
        var today = clock.Today;

        TaskItem Make(string title, TaskPriority priority, int dueInDays, Department dept, AppUser assignee, AppUser creator, string? description = null) =>
            new(title, description, priority, today.AddDays(dueInDays), dept.Id, assignee.Id, creator.Id, creator.FullName, TaskItem.WebSource, null, now);

        var tasks = new[]
        {
            Make("Renew SSL certificate for the intranet portal", TaskPriority.Critical, -2, it, staffIt, manager,
                "Certificate expires this week. Coordinate with the infrastructure team for the IIS binding update."),
            Make("Patch Windows Server 2022 application servers", TaskPriority.High, 1, it, staffIt, manager),
            Make("Review OWASP ZAP scan findings for the HR portal", TaskPriority.High, 3, it, manager, admin),
            Make("Prepare UAT test cases for the leave module", TaskPriority.Medium, 5, it, staffIt, manager),
            Make("Reconcile petty cash for October", TaskPriority.Medium, 0, finance, staffFin, staffFin),
            Make("Submit monthly VAT working papers", TaskPriority.High, -1, finance, staffFin, staffFin),
            Make("Update vendor contact list", TaskPriority.Low, 10, it, staffIt, staffIt),
        };

        tasks[1].ChangeStatus(WorkStatus.InProgress, now);
        tasks[2].ChangeStatus(WorkStatus.InProgress, now);
        tasks[6].ChangeStatus(WorkStatus.Done, now);
        db.Tasks.AddRange(tasks);
        await db.SaveChangesAsync(cancellationToken);

        tasks[0].AddComment(manager.Id, manager.FullName, "Vendor confirmed the new certificate will be issued today.", now);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded demo departments, users and tasks");
    }

    private async Task<AppUser> CreateUserAsync(string email, string fullName, int departmentId, string role, string password)
    {
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = fullName,
            DepartmentId = departmentId,
        };

        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not create demo user {email}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }

        await userManager.AddToRoleAsync(user, role);
        return user;
    }
}
