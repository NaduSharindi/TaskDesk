using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TaskDesk.Domain;

namespace TaskDesk.Data;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.Property(d => d.Code).HasMaxLength(10).IsRequired();
        builder.HasIndex(d => d.Code).IsUnique();
        builder.Property(d => d.Name).HasMaxLength(100).IsRequired();
    }
}

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.Property(u => u.FullName).HasMaxLength(100).IsRequired();
        builder.HasOne(u => u.Department)
            .WithMany()
            .HasForeignKey(u => u.DepartmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.DepartmentId);
    }
}

internal sealed class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
{
    public void Configure(EntityTypeBuilder<TaskItem> builder)
    {
        builder.ToTable("Tasks");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).HasMaxLength(TaskItem.TitleMaxLength).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(TaskItem.DescriptionMaxLength);

        // Priority stays numeric so ORDER BY Priority sorts Low < Medium < High < Critical.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.CreatedByName).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Source).HasMaxLength(60).IsRequired();
        builder.Property(t => t.ExternalReference).HasMaxLength(64);
        builder.Property(t => t.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.AssignedTo).WithMany().HasForeignKey(t => t.AssignedToId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(t => t.CreatedBy).WithMany().HasForeignKey(t => t.CreatedById).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(t => t.Comments).WithOne().HasForeignKey(c => c.TaskItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Comments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Indexes chosen from the main query patterns ("my tasks", "team board", "overdue").
        builder.HasIndex(t => new { t.AssignedToId, t.Status });
        builder.HasIndex(t => new { t.DepartmentId, t.Status, t.DueDate });
        builder.HasIndex(t => t.CreatedById);

        // Idempotency for API-created tasks (SQL Server adds "WHERE ExternalReference IS NOT NULL").
        builder.HasIndex(t => new { t.Source, t.ExternalReference }).IsUnique();
    }
}

internal sealed class TaskCommentConfiguration : IEntityTypeConfiguration<TaskComment>
{
    public void Configure(EntityTypeBuilder<TaskComment> builder)
    {
        builder.ToTable("TaskComments");
        builder.Property(c => c.AuthorName).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Text).HasMaxLength(TaskItem.CommentMaxLength).IsRequired();
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.AuthorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.TaskItemId, c.CreatedAtUtc });
    }
}

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.Property(a => a.UserName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Action).HasMaxLength(50).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.EntityId).HasMaxLength(100).IsRequired();
        builder.Property(a => a.CorrelationId).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(45);
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
        builder.HasIndex(a => a.TimestampUtc);
    }
}
