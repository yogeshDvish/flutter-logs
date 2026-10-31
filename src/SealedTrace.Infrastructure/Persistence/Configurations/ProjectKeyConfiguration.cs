using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class ProjectKeyConfiguration : IEntityTypeConfiguration<ProjectKey>
{
    public void Configure(EntityTypeBuilder<ProjectKey> builder)
    {
        builder.ToTable("ProjectKeys");

        builder.HasKey(key => key.Id);
        builder.Property(key => key.Id).ValueGeneratedNever();

        builder.Property(key => key.Algorithm).HasMaxLength(64).IsRequired();
        builder.Property(key => key.PublicKeyPem).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(key => key.PrivateKeyEncrypted).HasColumnType("varbinary(max)").IsRequired();
        builder.Property(key => key.CreatedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(key => key.ProjectId)
            .IsUnique()
            .HasDatabaseName("UX_ProjectKeys_Project");

        builder.HasOne(key => key.Project)
            .WithOne(project => project.ProjectKey)
            .HasForeignKey<ProjectKey>(key => key.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
