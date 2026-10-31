using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();

        builder.Property(project => project.Name).HasMaxLength(120).IsRequired();
        builder.Property(project => project.Slug).HasMaxLength(80).IsRequired();
        builder.Property(project => project.Description).HasMaxLength(500);
        builder.Property(project => project.Environment)
            .HasMaxLength(16)
            .HasDefaultValue("prod")
            .IsRequired();
        builder.Property(project => project.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(project => project.Slug)
            .IsUnique()
            .HasDatabaseName("UX_Projects_Slug");

        builder.HasIndex(project => new { project.OwnerId, project.CreatedAtUtc })
            .HasDatabaseName("IX_Projects_Owner_CreatedAt")
            .IsDescending(false, true);

        builder.HasOne(project => project.Owner)
            .WithMany(user => user.Projects)
            .HasForeignKey(project => project.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
