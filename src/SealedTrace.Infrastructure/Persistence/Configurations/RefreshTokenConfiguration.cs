using Arovyx.SealedTrace.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Arovyx.SealedTrace.Infrastructure.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TokenHash)
            .HasColumnType("binary(32)")
            .IsRequired();

        builder.Property(token => token.ReplacedByTokenHash)
            .HasColumnType("binary(32)");

        builder.Property(token => token.ExpiresAtUtc).HasColumnType("datetime2(3)");
        builder.Property(token => token.RevokedAtUtc).HasColumnType("datetime2(3)");

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_RefreshTokens_TokenHash");

        builder.HasIndex(token => new { token.UserId, token.ExpiresAtUtc })
            .HasDatabaseName("IX_RefreshTokens_User_ExpiresAt");

        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
