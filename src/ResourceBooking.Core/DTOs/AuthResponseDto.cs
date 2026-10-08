namespace ResourceBooking.Core.DTOs;

public class AuthResponseDto
{
    public bool IsSuccess { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAt { get; set; }
    public DateTime RefreshTokenExpiresAt { get; set; }
    // Transported only in an HttpOnly cookie, never in the JSON response.
    [System.Text.Json.Serialization.JsonIgnore]
    public string RefreshToken { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public string[] Errors { get; set; } = Array.Empty<string>();
}
