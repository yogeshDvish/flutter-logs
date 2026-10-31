using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace Arovyx.SealedTrace.Infrastructure.Persistence;

public sealed class SealedTraceDbContext(DbContextOptions<SealedTraceDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<ProjectKey> ProjectKeys => Set<ProjectKey>();

    public DbSet<IngestBatch> IngestBatches => Set<IngestBatch>();

    public DbSet<LogEntry> Logs => Set<LogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema("dbo");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SealedTraceDbContext).Assembly);
    }
}
