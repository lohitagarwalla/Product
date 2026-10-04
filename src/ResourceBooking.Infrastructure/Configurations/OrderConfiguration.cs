using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_TotalAmount", "[TotalAmount] >= 0");
            table.HasCheckConstraint("CK_Orders_Status", "[Status] IN ('Placed', 'Shipped', 'Delivered', 'Cancelled')");
        });
        builder.HasKey(o => o.Id);
        builder.Property(o => o.OrderNumber).HasMaxLength(36).IsRequired();
        builder.HasIndex(o => o.OrderNumber).IsUnique();
        builder.Property(o => o.UserId).HasMaxLength(450).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(o => new { o.UserId, o.RequestId }).IsUnique();
        builder.Property(o => o.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.Currency).HasMaxLength(3).IsRequired();
        builder.Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Property(o => o.CancellationReason).HasMaxLength(1000);
        builder.Property(o => o.CancelledByUserId).HasMaxLength(450);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(o => o.CancelledByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.HasIndex(o => new { o.UserId, o.CreatedAt, o.Id });
        builder.HasIndex(o => new { o.UserId, o.Status, o.CreatedAt, o.Id });
        builder.HasIndex(o => new { o.Status, o.CreatedAt, o.Id });
        builder.HasIndex(o => new { o.CreatedAt, o.Id });
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("OrderItems", table =>
        {
            table.HasCheckConstraint("CK_OrderItems_Quantity", "[Quantity] BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_OrderItems_Price", "[UnitPrice] >= 0 AND [LineTotal] = [UnitPrice] * [Quantity]");
        });
        builder.HasKey(i => i.Id);
        builder.HasOne(i => i.Order).WithMany(o => o.Items).HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Restrict);
        // No Product navigation: soft-deleted products must not filter out order items.
        builder.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.OrderId, i.ProductId }).IsUnique();
        builder.Property(i => i.ProductTitle).HasMaxLength(200).IsRequired();
        builder.Property(i => i.UnitPrice).HasPrecision(18, 2);
        builder.Property(i => i.LineTotal).HasPrecision(18, 2);
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.ToTable("OrderStatusHistory", table =>
            table.HasCheckConstraint("CK_OrderStatusHistory_Transition",
                "([PreviousStatus] IS NULL AND [NewStatus] = 'Placed') OR " +
                "([PreviousStatus] IS NOT NULL AND [PreviousStatus] = 'Placed' AND [NewStatus] IN ('Shipped', 'Cancelled')) OR " +
                "([PreviousStatus] IS NOT NULL AND [PreviousStatus] = 'Shipped' AND [NewStatus] IN ('Delivered', 'Cancelled'))"));
        builder.HasKey(h => h.Id);
        builder.HasOne(h => h.Order).WithMany(o => o.StatusHistory).HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(h => h.PreviousStatus).HasConversion<string>().HasMaxLength(20);
        builder.Property(h => h.NewStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(h => h.ChangedByUserId).HasMaxLength(450).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(h => h.Reason).HasMaxLength(1000);
        builder.HasIndex(h => new { h.OrderId, h.ChangedAtUtc, h.Id });
    }
}
