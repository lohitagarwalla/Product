# Login rate limiting

`POST /api/auth/login` uses ASP.NET Core's built-in partitioned sliding-window limiter. Defaults in `RateLimiting:Login` are 10 requests per 60 seconds, split into six segments, with no queue. All requests reaching the login limiter count, regardless of credentials. Rejected requests never reach the controller or create sessions.

The response is HTTP 429 with a JSON `error`, `Cache-Control: no-store`, and `Retry-After` in seconds. The header uses the limiter estimate when available, otherwise a conservative full-window delay. CORS exposes this header to the configured frontend origins. Clients should wait before retrying; they should not attempt token refresh in response to login 429s.

Configure `PermitLimit`, `WindowSeconds`, and `SegmentsPerWindow` under `RateLimiting:Login`, or use environment variables such as `RateLimiting__Login__PermitLimit`. Restart the application after changes. Invalid limits fail startup. The segmented window is an approximation, not an exact timestamp log.

Partitions use `HttpContext.Connection.RemoteIpAddress`; IPv4-mapped IPv6 addresses share the IPv4 partition. Requests without an address share a fallback partition. Arbitrary `X-Forwarded-For` headers are not trusted. Before deployment behind a reverse proxy, configure ASP.NET Core forwarded headers with explicitly trusted proxies/networks before the limiter; otherwise users behind that proxy share one bucket. Do not trust all forwarded headers indiscriminately.

Counters live in each API process and reset on restart. Multiple API instances need a separate distributed or gateway policy for a shared limit. Users behind a shared public IP share an allowance; tune limits for real traffic. This does not provide comprehensive DDoS protection.

This change limits login only. Registration, refresh, logout, JWT validation, and Identity lockout behavior are unchanged. Account failed-attempt tracking and lockout remain a separate follow-up.

Focused verification:

```powershell
dotnet test tests/ResourceBooking.IntegrationTests --configuration Release --filter 'FullyQualifiedName~LoginRateLimitTests|FullyQualifiedName~JwtConfigurationTests|FullyQualifiedName~RefreshTokenTests'
```
