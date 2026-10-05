using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class UserAddressConfiguration : IEntityTypeConfiguration<UserAddress>
{
    public void Configure(EntityTypeBuilder<UserAddress> builder)
    {
        builder.ToTable("UserAddresses");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).HasMaxLength(450).IsRequired();
        builder.HasOne(a => a.User).WithMany(u => u.Addresses)
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(a => a.Label).HasMaxLength(50);
        builder.Property(a => a.RecipientName).HasMaxLength(100).IsRequired();
        builder.Property(a => a.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(a => a.AddressLine2).HasMaxLength(200);
        builder.Property(a => a.City).HasMaxLength(100).IsRequired();
        builder.Property(a => a.State).HasMaxLength(100).IsRequired();
        builder.Property(a => a.PostalCode).HasMaxLength(20).IsRequired();
        builder.Property(a => a.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(a => a.RowVersion).IsRowVersion();
        builder.HasIndex(a => a.UserId).IsUnique().HasFilter("[IsDefault] = 1")
            .HasDatabaseName("IX_UserAddresses_OneDefaultPerUser");
        builder.HasIndex(a => new { a.UserId, a.CreatedAt, a.Id });
    }
}
