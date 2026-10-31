using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();

        builder.Property(user => user.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasDatabaseName("UX_users_email");

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(user => user.DisplayName)
            .HasMaxLength(80);

        builder.Property(user => user.Status)
            .HasConversion<byte>();

        builder.Property(user => user.EmailVerifiedAt).HasColumnType("datetime2");
        builder.Property(user => user.CreatedAt).HasColumnType("datetime2");
        builder.Property(user => user.UpdatedAt).HasColumnType("datetime2");
    }
}
