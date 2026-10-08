using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Core.Interfaces;

public interface IAccountService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    Task<AuthResponseDto> RefreshAsync(string? refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string? refreshToken, CancellationToken ct = default);
    Task<string> GenerateJwtTokenAsync(ApplicationUser user);
}
