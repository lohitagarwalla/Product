using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class ProductPriceHistoryConfiguration : IEntityTypeConfiguration<ProductPriceHistory>
{
    public void Configure(EntityTypeBuilder<ProductPriceHistory> builder)
    {
        builder.ToTable("ProductPriceHistory", table =>
        {
            table.HasCheckConstraint("CK_ProductPriceHistory_Price",
                "[NewPrice] >= 0 AND ([PreviousPrice] IS NULL OR [PreviousPrice] >= 0)");
            table.HasCheckConstraint("CK_ProductPriceHistory_Entry",
                "([EntryType] = 'Baseline' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NULL) OR " +
                "([EntryType] = 'Created' AND [PreviousPrice] IS NULL AND [ChangedByUserId] IS NOT NULL) OR " +
                "([EntryType] = 'PriceChanged' AND [PreviousPrice] IS NOT NULL AND " +
                "[PreviousPrice] <> [NewPrice] AND [ChangedByUserId] IS NOT NULL)");
        });
        builder.HasKey(h => h.Id);
        // No Product navigation on the audit entry: deleted products must not hide history.
        builder.HasOne<Product>().WithMany(p => p.PriceHistory).HasForeignKey(h => h.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(h => h.PreviousPrice).HasPrecision(18, 2);
        builder.Property(h => h.NewPrice).HasPrecision(18, 2);
        builder.Property(h => h.ChangedByUserId).HasMaxLength(450);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(h => h.ChangedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(h => h.EntryType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.HasIndex(h => new { h.ProductId, h.ChangedAtUtc, h.Id });
    }
}
