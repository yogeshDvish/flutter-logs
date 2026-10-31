using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> builder)
    {
        builder.ToTable("Logs");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).ValueGeneratedNever();

        builder.Property(log => log.ClientTs).HasColumnType("datetime2(3)");
        builder.Property(log => log.ReceivedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(log => log.Level).HasColumnType("tinyint");
        builder.Property(log => log.Tag).HasMaxLength(128).IsRequired();
        builder.Property(log => log.Message).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(log => log.Error).HasColumnType("nvarchar(max)");
        builder.Property(log => log.StackTrace).HasColumnType("nvarchar(max)");
        builder.Property(log => log.MetadataJson).HasColumnType("nvarchar(max)");
        builder.Property(log => log.UserId).HasMaxLength(128);
        builder.Property(log => log.AppVersion).HasMaxLength(32);
        builder.Property(log => log.Platform).HasMaxLength(16);

        builder.HasIndex(log => new { log.ProjectId, log.ClientTs, log.Id })
            .HasDatabaseName("IX_Logs_Project_ClientTs_Id")
            .IsDescending(false, true, true);

        builder.HasIndex(log => new { log.ProjectId, log.Level, log.ClientTs })
            .HasDatabaseName("IX_Logs_Project_Level_ClientTs")
            .IsDescending(false, false, true);

        builder.HasIndex(log => new { log.ProjectId, log.Tag })
            .HasDatabaseName("IX_Logs_Project_Tag");

        builder.HasIndex(log => new { log.ProjectId, log.SessionId, log.ClientTs })
            .HasDatabaseName("IX_Logs_Project_Session_ClientTs")
            .IsDescending(false, false, true);

        builder.HasIndex(log => new { log.ProjectId, log.UserId, log.ClientTs })
            .HasDatabaseName("IX_Logs_Project_User_ClientTs")
            .IsDescending(false, false, true);

        builder.HasIndex(log => new { log.ProjectId, log.ReceivedAtUtc })
            .HasDatabaseName("IX_Logs_Project_ReceivedAt")
            .IsDescending(false, true);

        builder.HasOne(log => log.Project)
            .WithMany(project => project.Logs)
            .HasForeignKey(log => log.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(log => log.Batch)
            .WithMany(batch => batch.Logs)
            .HasForeignKey(log => log.BatchId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
