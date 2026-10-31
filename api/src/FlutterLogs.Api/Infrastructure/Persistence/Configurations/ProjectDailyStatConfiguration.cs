using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class ProjectDailyStatConfiguration : IEntityTypeConfiguration<ProjectDailyStat>
{
    public void Configure(EntityTypeBuilder<ProjectDailyStat> builder)
    {
        builder.ToTable("project_daily_stats");

        builder.HasKey(stat => new { stat.ProjectId, stat.Day });

        builder.Property(stat => stat.Day).HasColumnType("date");

        builder.HasOne(stat => stat.Project)
            .WithMany(project => project.DailyStats)
            .HasForeignKey(stat => stat.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
