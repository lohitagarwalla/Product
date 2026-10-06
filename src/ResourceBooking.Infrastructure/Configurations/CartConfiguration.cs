using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UserId).HasMaxLength(450).IsRequired();
        builder.HasIndex(c => c.UserId).IsUnique();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(c => c.SelectedAddress).WithMany().HasForeignKey(c => c.SelectedAddressId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(c => c.RowVersion).IsRowVersion();
    }
}

public class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> builder)
    {
        builder.ToTable("CartItems", table => table.HasCheckConstraint("CK_CartItems_Quantity", "[Quantity] BETWEEN 1 AND 1000"));
        builder.HasKey(i => i.Id);
        builder.HasOne(i => i.Cart).WithMany(c => c.Items).HasForeignKey(i => i.CartId).OnDelete(DeleteBehavior.Cascade);
        // Avoid a navigation to Product: soft deletion must not hide saved cart items.
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.CartId, i.ProductId }).IsUnique();
    }
}
