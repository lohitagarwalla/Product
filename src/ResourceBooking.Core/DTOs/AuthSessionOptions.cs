namespace ResourceBooking.Core.DTOs;

public class AuthSessionOptions
{
    public const string SectionName = "AuthSession";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshSessionDays { get; set; } = 7;
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5173"];
}
