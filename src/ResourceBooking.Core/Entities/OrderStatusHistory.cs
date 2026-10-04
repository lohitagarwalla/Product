using ResourceBooking.Core.Enums;

namespace ResourceBooking.Core.Entities;

// Append-only audit entries do not have UpdatedAt or soft-delete fields.
public class OrderStatusHistory
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public OrderStatus? PreviousStatus { get; set; }
    public OrderStatus NewStatus { get; set; }
    public string ChangedByUserId { get; set; } = string.Empty;
    public DateTime ChangedAtUtc { get; set; }
    public string? Reason { get; set; }
}
