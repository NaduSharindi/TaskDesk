using TaskDesk.Domain;

namespace TaskDesk.Tests.Domain;

public sealed class TaskItemTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 4, 30, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 30);

    private static TaskItem NewTask(DateOnly? due = null) =>
        new("Patch servers", "Monthly patching", TaskPriority.High, due, 1, "u1", "u1", "User One", TaskItem.WebSource, null, Now);

    [Fact]
    public void New_task_starts_in_ToDo()
    {
        var task = NewTask();

        Assert.Equal(WorkStatus.ToDo, task.Status);
        Assert.True(task.IsOpen);
        Assert.Null(task.CompletedAtUtc);
    }

    [Fact]
    public void Completing_a_task_records_completion_time_and_reopening_clears_it()
    {
        var task = NewTask();

        task.ChangeStatus(WorkStatus.Done, Now);
        Assert.Equal(Now, task.CompletedAtUtc);
        Assert.False(task.IsOpen);

        task.ChangeStatus(WorkStatus.InProgress, Now.AddHours(1));
        Assert.Null(task.CompletedAtUtc);
    }

    [Theory]
    [InlineData(WorkStatus.Blocked, WorkStatus.Done)]
    [InlineData(WorkStatus.Blocked, WorkStatus.ToDo)]
    public void Invalid_transitions_are_rejected(WorkStatus from, WorkStatus to)
    {
        var task = NewTask();
        task.ChangeStatus(from, Now);

        Assert.Throws<BusinessRuleException>(() => task.ChangeStatus(to, Now));
    }

    [Fact]
    public void Closed_tasks_are_read_only()
    {
        var task = NewTask();
        task.ChangeStatus(WorkStatus.Cancelled, Now);

        Assert.Throws<BusinessRuleException>(() => task.UpdateDetails("New title", null, TaskPriority.Low, null, Now));
    }

    [Fact]
    public void Overdue_only_applies_to_open_tasks_with_a_past_due_date()
    {
        var overdue = NewTask(Today.AddDays(-1));
        var dueToday = NewTask(Today);
        var noDueDate = NewTask();

        Assert.True(overdue.IsOverdue(Today));
        Assert.False(dueToday.IsOverdue(Today));
        Assert.False(noDueDate.IsOverdue(Today));

        overdue.ChangeStatus(WorkStatus.Done, Now);
        Assert.False(overdue.IsOverdue(Today));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string title)
    {
        Assert.Throws<BusinessRuleException>(() =>
            new TaskItem(title, null, TaskPriority.Low, null, 1, null, null, "x", TaskItem.WebSource, null, Now));
    }

    [Fact]
    public void Every_change_rotates_the_concurrency_stamp()
    {
        var task = NewTask();
        var before = task.ConcurrencyStamp;

        task.ChangeStatus(WorkStatus.InProgress, Now);

        Assert.NotEqual(before, task.ConcurrencyStamp);
    }

    [Fact]
    public void Reference_is_zero_padded() => Assert.Equal("TD-00042", TaskItem.FormatReference(42));
}
