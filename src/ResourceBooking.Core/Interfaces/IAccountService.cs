using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Core.Interfaces;

public interface IAccountService
{
    Task<AuthResponseDto> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto> LoginAsync(LoginDto dto);
    Task LogoutAsync();
    Task<string> GenerateJwtTokenAsync(ApplicationUser user);
}