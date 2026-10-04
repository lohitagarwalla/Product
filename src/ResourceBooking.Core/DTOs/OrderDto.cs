using System.ComponentModel.DataAnnotations;
using ResourceBooking.Core.Enums;

namespace ResourceBooking.Core.DTOs;

public class OrderCreateDto : IValidatableObject
{
    public Guid RequestId { get; set; }

    [Required, MinLength(1), MaxLength(100)]
    public List<OrderItemCreateDto> Items { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RequestId == Guid.Empty)
            yield return new ValidationResult("A non-empty request ID is required.", [nameof(RequestId)]);
        if (Items is not null && (Items.Any(i => i is null) ||
            Items.Select(i => i.ProductId).Distinct().Count() != Items.Count))
            yield return new ValidationResult("Supply each product exactly once; items cannot be null.", [nameof(Items)]);
    }
}

public class OrderItemCreateDto
{
    [Range(1, int.MaxValue)] public int ProductId { get; set; }
    [Range(1, 1000)] public int Quantity { get; set; }
}

public class OrderStatusChangeDto
{
    // System.Text.Json represents byte[] as Base64 in JSON.
    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; set; } = [];
}

public class OrderCancelDto : OrderStatusChangeDto
{
    [StringLength(1000)] public string? Reason { get; set; }
}

public class OrderQueryDto : IValidatableObject
{
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
    [EnumDataType(typeof(OrderStatus))] public OrderStatus? Status { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (FromUtc.HasValue && ToUtc.HasValue && FromUtc >= ToUtc)
            yield return new ValidationResult("FromUtc must be earlier than ToUtc.", [nameof(ToUtc)]);
    }
}

public record OrderItemResponseDto(int Id, int ProductId, string ProductTitle, decimal UnitPrice,
    int Quantity, decimal LineTotal, string? ImageUrl);
public record OrderStatusHistoryResponseDto(int Id, OrderStatus? PreviousStatus, OrderStatus NewStatus,
    string ChangedByUserId, DateTime ChangedAtUtc, string? Reason, string? ChangedByFirstName = null);
public record OrderResponseDto(int Id, string OrderNumber, string UserId, OrderStatus Status,
    string Currency, decimal TotalAmount, DateTime CreatedAt, DateTime? ShippedAt, DateTime? DeliveredAt,
    DateTime? CancelledAt, string? CancelledByUserId, string? CancellationReason, byte[] RowVersion,
    IReadOnlyList<OrderItemResponseDto> Items, IReadOnlyList<OrderStatusHistoryResponseDto> StatusHistory,
    string? UserFirstName = null, string? CancelledByFirstName = null);
public record OrderSummaryDto(int Id, string OrderNumber, string UserId, OrderStatus Status,
    string Currency, decimal TotalAmount, int TotalQuantity, DateTime CreatedAt, string? UserFirstName = null);
public record OrderPageDto(IReadOnlyList<OrderSummaryDto> Items, int TotalCount, int Page, int PageSize);
public record OrderCreateResult(OrderResponseDto Order, bool IsReplay);
