using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace FlutterLogs.Api.Infrastructure.Persistence;

/// <summary>
/// Root persistence context. Entity sets are added phase by phase;
/// Phase 3 introduces projects, ingestion keys, daily stats, and audit events.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<EmailToken> EmailTokens => Set<EmailToken>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectKey> ProjectKeys => Set<ProjectKey>();

    public DbSet<ProjectDailyStat> ProjectDailyStats => Set<ProjectDailyStat>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
