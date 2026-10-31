using FlutterLogs.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlutterLogs.Api.Infrastructure.Persistence.Configurations;

public sealed class EmailTokenConfiguration : IEntityTypeConfiguration<EmailToken>
{
    public void Configure(EntityTypeBuilder<EmailToken> builder)
    {
        builder.ToTable("email_tokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.Purpose).HasConversion<byte>();

        builder.Property(token => token.TokenHash)
            .HasMaxLength(32)
            .IsFixedLength()
            .IsRequired();

        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("UX_email_tokens_hash");

        builder.HasIndex(token => new { token.UserId, token.Purpose })
            .HasDatabaseName("IX_email_tokens_user_purpose");

        builder.Property(token => token.CreatedAt).HasColumnType("datetime2");
        builder.Property(token => token.ExpiresAt).HasColumnType("datetime2");
        builder.Property(token => token.UsedAt).HasColumnType("datetime2");

        builder.HasOne(token => token.User)
            .WithMany(user => user.EmailTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
