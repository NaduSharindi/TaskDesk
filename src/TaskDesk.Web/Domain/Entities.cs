using Microsoft.AspNetCore.Identity;

namespace TaskDesk.Domain;

public class Department
{
    private Department()
    {
    }

    public Department(string code, string name)
    {
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }

    public int Id { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
}

/// <summary>Office staff member. Extends ASP.NET Core Identity (password hashing, lockout, security stamp).</summary>
public class AppUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;

    public int DepartmentId { get; set; }

    public Department? Department { get; set; }

    public bool IsActive { get; set; } = true;
}

public class TaskComment
{
    private TaskComment()
    {
    }

    internal TaskComment(int taskItemId, string? authorId, string authorName, string text, DateTime createdAtUtc)
    {
        TaskItemId = taskItemId;
        AuthorId = authorId;
        AuthorName = authorName;
        Text = text;
        CreatedAtUtc = createdAtUtc;
    }

    public int Id { get; private set; }

    public int TaskItemId { get; private set; }

    /// <summary>Null when the comment came from an integrated system rather than a user.</summary>
    public string? AuthorId { get; private set; }

    public string AuthorName { get; private set; } = string.Empty;

    public string Text { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }
}

/// <summary>Append-only audit record produced automatically by AppDbContext on every save.</summary>
public class AuditEntry
{
    private AuditEntry()
    {
    }

    public AuditEntry(
        DateTime timestampUtc,
        string userName,
        string action,
        string entityName,
        string entityId,
        string? changes,
        string? correlationId,
        string? ipAddress)
    {
        TimestampUtc = timestampUtc;
        UserName = Truncate(userName, 256);
        Action = Truncate(action, 50);
        EntityName = Truncate(entityName, 100);
        EntityId = Truncate(entityId, 100);
        Changes = changes;
        CorrelationId = correlationId;
        IpAddress = ipAddress;
    }

    public long Id { get; private set; }

    public DateTime TimestampUtc { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string EntityName { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    /// <summary>JSON describing the changed fields (old and new values).</summary>
    public string? Changes { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? IpAddress { get; private set; }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
