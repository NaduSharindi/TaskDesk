using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TaskDesk.Common;
using TaskDesk.Domain;

namespace TaskDesk.Data;

/// <summary>
/// EF Core context (Identity + TaskDesk tables). Every SaveChanges automatically writes
/// AuditEntry rows (who / what / when / old -> new values) in the SAME database transaction.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser)
    : IdentityDbContext<AppUser>(options)
{
    private static readonly HashSet<Type> AuditedTypes = [typeof(TaskItem), typeof(TaskComment), typeof(Department)];
    private static readonly HashSet<string> NotAuditedProperties = [nameof(TaskItem.ConcurrencyStamp), nameof(TaskItem.UpdatedAtUtc)];

    private static readonly JsonSerializerOptions AuditJson = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public DbSet<Department> Departments => Set<Department>();

    public DbSet<TaskItem> Tasks => Set<TaskItem>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var pending = CaptureAudit();
        if (pending.Count == 0)
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        using var transaction = Database.CurrentTransaction is null ? Database.BeginTransaction() : null;
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        AuditEntries.AddRange(pending.Select(ToAuditEntry));
        base.SaveChanges(acceptAllChangesOnSuccess);
        transaction?.Commit();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var pending = CaptureAudit();
        if (pending.Count == 0)
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        // Business rows are saved first so database-generated keys (e.g. new task IDs) are known,
        // then the audit rows. Both commit together or not at all.
        await using IDbContextTransaction? transaction = Database.CurrentTransaction is null
            ? await Database.BeginTransactionAsync(cancellationToken)
            : null;

        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        AuditEntries.AddRange(pending.Select(ToAuditEntry));
        await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return result;
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // ASP.NET Core Identity tables
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    private List<PendingAudit> CaptureAudit()
    {
        ChangeTracker.DetectChanges();
        var pending = new List<PendingAudit>();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (!AuditedTypes.Contains(entry.Entity.GetType())
                || entry.State is EntityState.Unchanged or EntityState.Detached)
            {
                continue;
            }

            var changes = new Dictionary<string, object?>();
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.IsPrimaryKey() || NotAuditedProperties.Contains(property.Metadata.Name))
                {
                    continue;
                }

                switch (entry.State)
                {
                    case EntityState.Added:
                        changes[property.Metadata.Name] = property.CurrentValue;
                        break;
                    case EntityState.Deleted:
                        changes[property.Metadata.Name] = property.OriginalValue;
                        break;
                    case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                        changes[property.Metadata.Name] = new { From = property.OriginalValue, To = property.CurrentValue };
                        break;
                }
            }

            if (entry.State == EntityState.Modified && changes.Count == 0)
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Deleted => "Deleted",
                _ => "Updated",
            };

            pending.Add(new PendingAudit(entry, action, changes));
        }

        return pending;
    }

    private AuditEntry ToAuditEntry(PendingAudit pending)
    {
        var key = pending.Entry.Metadata.FindPrimaryKey()!;
        var entityId = string.Join(",", key.Properties.Select(p => pending.Entry.Property(p.Name).CurrentValue));

        return new AuditEntry(
            DateTime.UtcNow,
            currentUser.UserName,
            pending.Action,
            pending.Entry.Metadata.ClrType.Name,
            entityId,
            JsonSerializer.Serialize(pending.Changes, AuditJson),
            currentUser.CorrelationId,
            currentUser.IpAddress);
    }

    private sealed record PendingAudit(EntityEntry Entry, string Action, Dictionary<string, object?> Changes);
}

/// <summary>Stores DateTime as UTC and marks values read back as UTC.</summary>
internal sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
{
}
