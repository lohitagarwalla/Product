using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ResourceBooking.Web.RateLimiting;

public sealed class LoginRateLimitPolicy(IOptions<LoginRateLimitOptions> options) : IRateLimiterPolicy<string>
{
    public const string Name = "login";
    private readonly LoginRateLimitOptions settings = options.Value;

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Use the connection address, never a caller-supplied forwarding header.
        // A trusted proxy must be configured separately before deploying behind one.
        var address = httpContext.Connection.RemoteIpAddress;
        if (address?.IsIPv4MappedToIPv6 == true) address = address.MapToIPv4();
        var key = address?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            SegmentsPerWindow = settings.SegmentsPerWindow,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => async (context, ct) =>
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        // Some limiter leases do not supply an estimate; a full window is a conservative fallback.
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, Math.Ceiling(retryAfter.TotalSeconds)) : settings.WindowSeconds;
        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        response.Headers.CacheControl = "no-store";
        await response.WriteAsJsonAsync(new
        {
            error = "Too many login requests. Please try again later."
        }, cancellationToken: ct);
    };
}
