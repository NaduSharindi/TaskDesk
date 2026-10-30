using Microsoft.EntityFrameworkCore;
using TaskDesk.Common;
using TaskDesk.Data;
using TaskDesk.Domain;

namespace TaskDesk.Services;

/// <summary>Explicit audit events that are not entity changes (sign-in, sign-out, failed logins).</summary>
public sealed class AuditService(AppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task RecordAsync(
        string action, string entityName, string entityId, string? userNameOverride, CancellationToken cancellationToken)
    {
        db.AuditEntries.Add(new AuditEntry(
            clock.UtcNow,
            userNameOverride ?? currentUser.UserName,
            action,
            entityName,
            entityId,
            changes: null,
            currentUser.CorrelationId,
            currentUser.IpAddress));

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<PagedResult<AuditEntry>> SearchAsync(
        string? userName, string? action, int page, int pageSize, CancellationToken cancellationToken)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var query = db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(userName))
        {
            query = query.Where(a => a.UserName.Contains(userName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action.Trim());
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEntry>(items, page, pageSize, total);
    }
}
