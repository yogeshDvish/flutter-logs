using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(32)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_refresh_tokens_hash");

        builder.HasIndex(token => token.UserId)
            .HasDatabaseName("IX_refresh_tokens_user");

        builder.HasIndex(token => token.FamilyId)
            .HasDatabaseName("IX_refresh_tokens_family");

        builder.Property(token => token.CreatedAt).HasColumnType("datetime2");
        builder.Property(token => token.ExpiresAt).HasColumnType("datetime2");
        builder.Property(token => token.RevokedAt).HasColumnType("datetime2");

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
