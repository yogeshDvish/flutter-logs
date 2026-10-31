using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class IngestBatchConfiguration : IEntityTypeConfiguration<IngestBatch>
{
    public void Configure(EntityTypeBuilder<IngestBatch> builder)
    {
        builder.ToTable("IngestBatches");

        builder.HasKey(batch => batch.BatchId);
        builder.Property(batch => batch.BatchId).ValueGeneratedNever();

        builder.Property(batch => batch.ReceivedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(batch => new { batch.ProjectId, batch.ReceivedAtUtc })
            .HasDatabaseName("IX_IngestBatches_Project_ReceivedAt")
            .IsDescending(false, true);

        builder.HasOne(batch => batch.Project)
            .WithMany(project => project.IngestBatches)
            .HasForeignKey(batch => batch.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
