using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("projects");

        builder.HasKey(project => project.Id);
        builder.Property(project => project.Id).ValueGeneratedNever();

        builder.Property(project => project.Name)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(project => project.Description)
            .HasMaxLength(500);

        builder.Property(project => project.IngestEnabled)
            .HasDefaultValue(true);

        builder.Property(project => project.CreatedAt).HasColumnType("datetime2");
        builder.Property(project => project.UpdatedAt).HasColumnType("datetime2");
        builder.Property(project => project.ArchivedAt).HasColumnType("datetime2");

        builder.HasIndex(project => new { project.OwnerUserId, project.ArchivedAt })
            .HasDatabaseName("IX_projects_owner");

        builder.HasOne(project => project.Owner)
            .WithMany()
            .HasForeignKey(project => project.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
