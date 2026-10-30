using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;
using TaskDesk.Integration;
using TaskDesk.Services;

namespace TaskDesk.Tests.TestSupport;

public sealed class FakeClock : IClock
{
    // 30 Oct 2026 10:00 in Sri Lanka
    public DateTime UtcNow { get; set; } = new(2026, 10, 30, 4, 30, 0, DateTimeKind.Utc);

    public DateOnly Today => DateOnly.FromDateTime(UtcNow.AddMinutes(330));

    public DateTime ToOfficeTime(DateTime utc) => utc.AddMinutes(330);
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public string? UserId { get; set; }

    public string UserName { get; set; } = "test-user";

    public string? IpAddress => "127.0.0.1";

    public string? CorrelationId => "test-correlation";
}

public sealed class FakeHolidayCalendar : IHolidayCalendar
{
    public List<Holiday> Holidays { get; } = [];

    public Task<IReadOnlyList<Holiday>> GetHolidaysAsync(int year, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Holiday>>(Holidays.Where(h => h.Date.Year == year).ToList());
}

/// <summary>
/// Real relational database (SQLite in-memory) with two departments and four users, so tests exercise
/// actual SQL, unique indexes, concurrency tokens and the audit transaction.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDatabase()
    {
        _connection.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();

        var it = new Department("IT", "Information Technology");
        var finance = new Department("FIN", "Finance");
        db.Departments.AddRange(it, finance);
        db.SaveChanges();

        ItDepartmentId = it.Id;
        FinanceDepartmentId = finance.Id;

        db.Users.AddRange(
            NewUser(AdminId, "admin@test.local", "Admin User", it.Id),
            NewUser(ItManagerId, "manager.it@test.local", "IT Manager", it.Id),
            NewUser(ItStaffId, "staff.it@test.local", "IT Staff", it.Id),
            NewUser(ItStaff2Id, "staff2.it@test.local", "IT Staff Two", it.Id),
            NewUser(FinStaffId, "staff.fin@test.local", "Finance Staff", finance.Id));
        db.SaveChanges();

        Admin = new UserScope(AdminId, "admin@test.local", "Admin User", it.Id, IsAdmin: true, IsManager: false);
        ItManager = new UserScope(ItManagerId, "manager.it@test.local", "IT Manager", it.Id, IsAdmin: false, IsManager: true);
        ItStaff = new UserScope(ItStaffId, "staff.it@test.local", "IT Staff", it.Id, IsAdmin: false, IsManager: false);
        ItStaff2 = new UserScope(ItStaff2Id, "staff2.it@test.local", "IT Staff Two", it.Id, IsAdmin: false, IsManager: false);
        FinStaff = new UserScope(FinStaffId, "staff.fin@test.local", "Finance Staff", finance.Id, IsAdmin: false, IsManager: false);
    }

    public const string AdminId = "u-admin";
    public const string ItManagerId = "u-it-manager";
    public const string ItStaffId = "u-it-staff";
    public const string ItStaff2Id = "u-it-staff2";
    public const string FinStaffId = "u-fin-staff";

    public int ItDepartmentId { get; }

    public int FinanceDepartmentId { get; }

    public UserScope Admin { get; }

    public UserScope ItManager { get; }

    public UserScope ItStaff { get; }

    public UserScope ItStaff2 { get; }

    public UserScope FinStaff { get; }

    public FakeCurrentUser CurrentUser { get; } = new();

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, CurrentUser);

    public void Dispose() => _connection.Dispose();

    private static AppUser NewUser(string id, string email, string name, int departmentId) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        FullName = name,
        DepartmentId = departmentId,
        SecurityStamp = Guid.NewGuid().ToString(),
    };
}
