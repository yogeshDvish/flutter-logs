using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("audit_events");

        builder.HasKey(audit => audit.Id);
        builder.Property(audit => audit.Id).ValueGeneratedOnAdd();

        builder.Property(audit => audit.Action)
            .HasMaxLength(60)
            .IsRequired();

        builder.Property(audit => audit.TargetType).HasMaxLength(40);
        builder.Property(audit => audit.TargetId).HasMaxLength(64);

        builder.Property(audit => audit.CreatedAt).HasColumnType("datetime2");

        builder.HasIndex(audit => new { audit.UserId, audit.CreatedAt })
            .HasDatabaseName("IX_audit_events_user_created");

        builder.HasIndex(audit => new { audit.ProjectId, audit.CreatedAt })
            .HasDatabaseName("IX_audit_events_project_created");
    }
}
