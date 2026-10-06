using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Core.Interfaces;

public interface ICartService
{
    Task<CartResponseDto> GetAsync(string userId, CancellationToken ct);
    Task<CartResponseDto> SetItemAsync(int productId, CartItemWriteDto dto, string userId, CancellationToken ct);
    Task<CartResponseDto> RemoveItemAsync(int productId, string userId, CancellationToken ct);
    Task<CartResponseDto> SetAddressAsync(CartAddressWriteDto dto, string userId, CancellationToken ct);
    Task<CartResponseDto> ClearItemsAsync(string userId, CancellationToken ct);
}
