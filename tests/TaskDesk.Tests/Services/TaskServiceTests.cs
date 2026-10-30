using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskDesk.Data;
using TaskDesk.Domain;
using TaskDesk.Integration;
using TaskDesk.Services;
using TaskDesk.Tests.TestSupport;

namespace TaskDesk.Tests.Services;

public sealed class TaskServiceTests : IDisposable
{
    private readonly TestDatabase _database = new();
    private readonly FakeClock _clock = new();
    private readonly FakeHolidayCalendar _holidays = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task Staff_task_is_assigned_to_themselves_by_default_and_audited()
    {
        await using var db = _database.CreateContext();
        _database.CurrentUser.UserName = _database.ItStaff.UserName;

        var result = await Service(db).CreateAsync(_database.ItStaff, Input("Order new toner"), CancellationToken.None);

        await using var verify = _database.CreateContext();
        var task = await verify.Tasks.SingleAsync(t => t.Id == result.TaskId);
        Assert.Equal(TestDatabase.ItStaffId, task.AssignedToId);
        Assert.Equal(_database.ItDepartmentId, task.DepartmentId);

        var audit = await verify.AuditEntries.SingleAsync(a => a.EntityName == "TaskItem" && a.EntityId == result.TaskId.ToString());
        Assert.Equal("Created", audit.Action);
        Assert.Equal("staff.it@test.local", audit.UserName);
        Assert.Contains("Order new toner", audit.Changes);
    }

    [Fact]
    public async Task Staff_cannot_assign_tasks_to_colleagues()
    {
        await using var db = _database.CreateContext();

        await Assert.ThrowsAsync<ForbiddenException>(() => Service(db).CreateAsync(
            _database.ItStaff, Input("Not allowed", assignee: TestDatabase.ItStaff2Id), CancellationToken.None));
    }

    [Fact]
    public async Task Manager_can_assign_within_department_but_not_outside()
    {
        await using var db = _database.CreateContext();
        var service = Service(db);

        var ok = await service.CreateAsync(_database.ItManager, Input("Team task", assignee: TestDatabase.ItStaffId), CancellationToken.None);
        Assert.True(ok.TaskId > 0);

        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(
            _database.ItManager, Input("Other dept", assignee: TestDatabase.FinStaffId), CancellationToken.None));
    }

    [Fact]
    public async Task Visibility_follows_role_rules()
    {
        int itTaskId;
        int finTaskId;
        await using (var db = _database.CreateContext())
        {
            var service = Service(db);
            itTaskId = (await service.CreateAsync(_database.ItManager, Input("IT task", assignee: TestDatabase.ItStaffId), CancellationToken.None)).TaskId;
            finTaskId = (await service.CreateAsync(_database.FinStaff, Input("Finance task"), CancellationToken.None)).TaskId;
        }

        await using var read = _database.CreateContext();
        var reader = Service(read);

        async Task<List<int>> VisibleIds(UserScope user) => await reader.VisibleTo(user).Select(t => t.Id).ToListAsync();

        Assert.Equal(new[] { itTaskId, finTaskId }.Order(), (await VisibleIds(_database.Admin)).Order());
        Assert.Equal(new[] { itTaskId }, await VisibleIds(_database.ItManager));
        Assert.Equal(new[] { itTaskId }, await VisibleIds(_database.ItStaff));
        Assert.Empty(await VisibleIds(_database.ItStaff2));
        Assert.Equal(new[] { finTaskId }, await VisibleIds(_database.FinStaff));

        // Another staff member cannot open the task - and gets 404, not 403 (no information leak).
        await Assert.ThrowsAsync<NotFoundException>(() => reader.GetDetailsAsync(_database.ItStaff2, itTaskId, CancellationToken.None));
    }

    [Fact]
    public async Task Stale_edit_is_rejected_with_a_concurrency_error()
    {
        int id;
        Guid staleStamp;
        await using (var db = _database.CreateContext())
        {
            id = (await Service(db).CreateAsync(_database.ItStaff, Input("Original"), CancellationToken.None)).TaskId;
            staleStamp = (await db.Tasks.AsNoTracking().SingleAsync(t => t.Id == id)).ConcurrencyStamp;
        }

        await using (var db = _database.CreateContext())
        {
            await Service(db).ChangeStatusAsync(_database.ItStaff, id, WorkStatus.InProgress, CancellationToken.None);
        }

        await using var db2 = _database.CreateContext();
        await Assert.ThrowsAsync<ConcurrencyException>(() =>
            Service(db2).UpdateAsync(_database.ItStaff, id, Input("Overwrite"), staleStamp, CancellationToken.None));
    }

    [Fact]
    public async Task Status_change_is_audited_with_old_and_new_values()
    {
        int id;
        await using (var db = _database.CreateContext())
        {
            id = (await Service(db).CreateAsync(_database.ItStaff, Input("Audit me"), CancellationToken.None)).TaskId;
        }

        await using (var db = _database.CreateContext())
        {
            await Service(db).ChangeStatusAsync(_database.ItStaff, id, WorkStatus.Done, CancellationToken.None);
        }

        await using var verify = _database.CreateContext();
        var update = await verify.AuditEntries.SingleAsync(a => a.EntityName == "TaskItem" && a.Action == "Updated");
        using var json = JsonDocument.Parse(update.Changes!);
        Assert.Equal("ToDo", json.RootElement.GetProperty("Status").GetProperty("From").GetString());
        Assert.Equal("Done", json.RootElement.GetProperty("Status").GetProperty("To").GetString());
    }

    [Fact]
    public async Task Past_due_dates_are_rejected_and_holidays_produce_a_warning()
    {
        await using var db = _database.CreateContext();
        var service = Service(db);

        await Assert.ThrowsAsync<BusinessRuleException>(() => service.CreateAsync(
            _database.ItStaff, Input("Too late", due: _clock.Today.AddDays(-1)), CancellationToken.None));

        var christmas = new DateOnly(2026, 12, 25); // a Friday
        _holidays.Holidays.Add(new Holiday(christmas, "Christmas Day"));

        var result = await service.CreateAsync(_database.ItStaff, Input("Holiday task", due: christmas), CancellationToken.None);
        Assert.Contains(result.Warnings, w => w.Contains("Christmas Day"));
    }

    [Fact]
    public async Task Search_filters_overdue_tasks_and_supports_reference_lookup()
    {
        int overdueId;
        await using (var db = _database.CreateContext())
        {
            var service = Service(db);
            overdueId = (await service.CreateAsync(_database.ItStaff, Input("Will be overdue", due: _clock.Today), CancellationToken.None)).TaskId;
            await service.CreateAsync(_database.ItStaff, Input("Not overdue", due: _clock.Today.AddDays(5)), CancellationToken.None);
        }

        _clock.UtcNow = _clock.UtcNow.AddDays(2); // time passes

        await using var read = _database.CreateContext();
        var reader = Service(read);

        var overdue = await reader.SearchAsync(_database.ItStaff, new TaskFilter { OverdueOnly = true }, CancellationToken.None);
        Assert.Equal(overdueId, Assert.Single(overdue.Items).Id);
        Assert.True(overdue.Items[0].IsOverdue);

        var byReference = await reader.SearchAsync(
            _database.ItStaff, new TaskFilter { Search = TaskItem.FormatReference(overdueId) }, CancellationToken.None);
        Assert.Equal(overdueId, Assert.Single(byReference.Items).Id);
    }

    private TaskService Service(AppDbContext db) => new(db, _clock, _holidays, NullLogger<TaskService>.Instance);

    private static TaskInput Input(string title, string? assignee = null, DateOnly? due = null) =>
        new(title, "Created by a unit test", TaskPriority.Medium, due, assignee);
}
