using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.KeyHash)
            .HasColumnType("binary(32)")
            .IsRequired();

        builder.Property(key => key.KeyPrefix).HasMaxLength(16).IsRequired();
        builder.Property(key => key.CreatedAtUtc).HasColumnType("datetime2(3)");
        builder.Property(key => key.RevokedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(key => key.KeyHash)
            .IsUnique()
            .HasDatabaseName("UX_ApiKeys_KeyHash");

        builder.HasIndex(key => new { key.ProjectId, key.RevokedAtUtc })
            .HasDatabaseName("IX_ApiKeys_Project_Revoked");

        builder.HasOne(key => key.Project)
            .WithMany(project => project.ApiKeys)
            .HasForeignKey(key => key.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
