namespace ResourceBooking.Core.DTOs;

public sealed class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "ResourceBookingApi";
    public string Audience { get; set; } = "ResourceBookingClients";
}
