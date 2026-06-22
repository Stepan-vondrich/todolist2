using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TodoApi.Models;

namespace TodoApi.Data;

/// <summary>
/// Ensures all DateTime values read from SQLite are tagged as UTC so that
/// System.Text.Json serialises them with a trailing 'Z'.  Without this,
/// SQLite returns Kind=Unspecified and the browser treats the timestamp as
/// local time instead of UTC, causing an offset equal to the local timezone.
/// </summary>
class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    v => v.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : v.ToUniversalTime(),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> Todos => Set<TodoItem>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<CommentAttachment> CommentAttachments => Set<CommentAttachment>();
    public DbSet<TaskSession> TaskSessions => Set<TaskSession>();
    public DbSet<TaskOverlap> TaskOverlaps => Set<TaskOverlap>();
    public DbSet<TaskLog> TaskLogs => Set<TaskLog>();
    public DbSet<FilterBookmark> FilterBookmarks => Set<FilterBookmark>();
    public DbSet<TaskManifest> TaskManifests => Set<TaskManifest>();
    public DbSet<PlannerSettings> PlannerSettings => Set<PlannerSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        // Disable cascade delete for TaskOverlap's two FK paths to avoid SQLite cycle issues
        mb.Entity<TaskOverlap>()
            .HasOne<TaskSession>()
            .WithMany()
            .HasForeignKey(o => o.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<TaskOverlap>()
            .HasOne<TaskSession>()
            .WithMany()
            .HasForeignKey(o => o.OverlappingSessionId)
            .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<TaskSession>()
            .HasOne<TodoItem>()
            .WithMany()
            .HasForeignKey(s => s.TodoId)
            .OnDelete(DeleteBehavior.Cascade);

        mb.Entity<TaskLog>()
            .HasOne<TodoItem>()
            .WithMany()
            .HasForeignKey(l => l.TodoId)
            .OnDelete(DeleteBehavior.Cascade);

        // TaskManifest is 1:1 with TodoItem; cascade-delete with the owning task.
        mb.Entity<TaskManifest>()
            .HasOne<TodoItem>()
            .WithMany()
            .HasForeignKey(m => m.TodoId)
            .OnDelete(DeleteBehavior.Cascade);
        mb.Entity<TaskManifest>().HasIndex(m => m.TodoId).IsUnique();
        mb.Entity<TaskManifest>().HasIndex(m => m.Slug).IsUnique();
    }
}
