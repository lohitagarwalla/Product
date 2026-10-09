namespace ResourceBooking.Web.RateLimiting;

public sealed class LoginRateLimitOptions
{
    public const string SectionName = "RateLimiting:Login";
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
    public int SegmentsPerWindow { get; set; } = 6;
}
