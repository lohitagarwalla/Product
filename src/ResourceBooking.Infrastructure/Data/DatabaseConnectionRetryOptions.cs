namespace ResourceBooking.Infrastructure.Data;

public sealed class DatabaseConnectionRetryOptions
{
    public const string SectionName = "DatabaseConnectionRetry";
    public int TimeoutSeconds { get; set; } = 90;
}
