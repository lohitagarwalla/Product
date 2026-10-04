using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Enums;

namespace ResourceBooking.Core.Interfaces;

public interface IOrderService
{
    Task<OrderCreateResult> CreateAsync(OrderCreateDto dto, string userId, CancellationToken ct);
    Task<OrderPageDto> ListAsync(OrderQueryDto query, string userId, bool includeAllUsers, CancellationToken ct);
    Task<OrderResponseDto> GetAsync(int id, string userId, bool isAdmin, CancellationToken ct);
    Task<OrderResponseDto> ChangeStatusAsync(int id, OrderStatus target, byte[] rowVersion,
        string? reason, string userId, bool isAdmin, CancellationToken ct);
}
