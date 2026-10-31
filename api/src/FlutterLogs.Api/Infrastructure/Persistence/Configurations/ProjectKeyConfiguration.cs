using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class ProjectKeyConfiguration : IEntityTypeConfiguration<ProjectKey>
{
    public void Configure(EntityTypeBuilder<ProjectKey> builder)
    {
        builder.ToTable("project_keys");

        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.Prefix)
            .HasMaxLength(8)
            .IsFixedLength()
            .IsRequired();

        builder.Property(key => key.KeyHash)
            .HasMaxLength(32)
            .IsFixedLength()
            .IsRequired();

        builder.Property(key => key.Label)
            .HasMaxLength(60);

        builder.Property(key => key.CreatedAt).HasColumnType("datetime2");
        builder.Property(key => key.LastUsedAt).HasColumnType("datetime2");
        builder.Property(key => key.GraceUntil).HasColumnType("datetime2");
        builder.Property(key => key.RevokedAt).HasColumnType("datetime2");

        builder.HasIndex(key => key.Prefix)
            .IsUnique()
            .HasDatabaseName("UX_project_keys_prefix");

        builder.HasIndex(key => new { key.ProjectId, key.RevokedAt })
            .HasDatabaseName("IX_project_keys_project");

        builder.HasOne(key => key.Project)
            .WithMany(project => project.Keys)
            .HasForeignKey(key => key.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
