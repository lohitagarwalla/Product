using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class RefreshSessionConfiguration : IEntityTypeConfiguration<RefreshSession>
{
    public void Configure(EntityTypeBuilder<RefreshSession> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.UserId).HasMaxLength(450).IsRequired();
        builder.Property(x => x.SecurityStamp).HasMaxLength(256).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.ExpiresAt);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsUnicode(false).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasOne(x => x.Session).WithMany().HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
