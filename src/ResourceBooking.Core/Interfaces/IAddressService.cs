using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Core.Interfaces;

public interface IAddressService
{
    Task<IReadOnlyList<AddressResponseDto>> ListAsync(string userId, CancellationToken ct);
    Task<AddressResponseDto> GetAsync(int id, string userId, CancellationToken ct);
    Task<AddressResponseDto> CreateAsync(AddressCreateDto dto, string userId, CancellationToken ct);
    Task<AddressResponseDto> UpdateAsync(int id, AddressUpdateDto dto, string userId, CancellationToken ct);
    Task<AddressResponseDto> SetDefaultAsync(int id, byte[] rowVersion, string userId, CancellationToken ct);
    Task DeleteAsync(int id, byte[] rowVersion, string userId, CancellationToken ct);
}
