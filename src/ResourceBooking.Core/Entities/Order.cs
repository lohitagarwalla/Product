using ResourceBooking.Core.Enums;

namespace ResourceBooking.Core.Entities;

public class Order : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Placed;
    public string Currency { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
    public string? CancelledByUserId { get; set; }
    public string? CancellationReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
}
