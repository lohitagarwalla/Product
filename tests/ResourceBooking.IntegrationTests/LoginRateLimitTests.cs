using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class LoginRateLimitTests
{
    private static WebApplicationFactory<Program> Factory(int permits = 2, int window = 60, int segments = 6) =>
        new CustomWebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimiting:Login:PermitLimit"] = permits.ToString(),
                ["RateLimiting:Login:WindowSeconds"] = window.ToString(),
                ["RateLimiting:Login:SegmentsPerWindow"] = segments.ToString()
            })));

    private static async Task<HttpContext> Login(WebApplicationFactory<Program> factory, string ip = "192.0.2.1",
        string email = "missing@example.com", string password = "WrongPassword123!")
    {
        using var body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new LoginDto
        { Email = email, Password = password })));
        return await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            context.Request.Method = "POST";
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("localhost");
            context.Request.Path = "/api/auth/login";
            context.Request.ContentType = "application/json";
            context.Request.Body = body;
            context.Request.ContentLength = body.Length;
            context.Request.Headers.Origin = "http://localhost:5173";
            // Changing untrusted headers must not create a new partition.
            context.Request.Headers["X-Forwarded-For"] = $"198.51.100.{Random.Shared.Next(1, 254)}";
        });
    }

    [Fact]
    public async Task ConcurrentRequests_RespectLimit_AndDifferentIpsAreIndependent()
    {
        using var factory = Factory();
        using var client = factory.CreateClient();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Login(factory)));
        Assert.Equal(2, results.Count(r => r.Response.StatusCode == 401));
        Assert.Equal(6, results.Count(r => r.Response.StatusCode == 429));
        var rejected = results.First(r => r.Response.StatusCode == 429).Response;
        Assert.True(int.Parse(rejected.Headers.RetryAfter.ToString()) > 0);
        Assert.Equal("no-store", rejected.Headers.CacheControl.ToString());
        Assert.Contains("Retry-After", rejected.Headers.AccessControlExposeHeaders.ToString());
        Assert.Equal(401, (await Login(factory, "192.0.2.2")).Response.StatusCode);
        Assert.Equal(429, (await Login(factory, "::ffff:192.0.2.1")).Response.StatusCode);
    }

    [Fact]
    public async Task BlockedCorrectPassword_CreatesNoSession_AndOtherEndpointsStillRun()
    {
        using var factory = Factory(1);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        var email = $"{Guid.NewGuid():N}@example.com";
        var registration = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        { Email = email, Password = "Password123!", ConfirmPassword = "Password123!", FirstName = "Rate", LastName = "Test" });
        registration.EnsureSuccessStatusCode();
        Assert.Equal(200, (await Login(factory, email: email, password: "Password123!")).Response.StatusCode);
        var blocked = await Login(factory, email: email, password: "Password123!");
        Assert.Equal(429, blocked.Response.StatusCode);
        Assert.False(blocked.Response.Headers.ContainsKey("Set-Cookie"));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.RefreshSessions.CountAsync());
        Assert.Equal(0, (await db.Users.SingleAsync(u => u.Email == email)).AccessFailedCount);
        // Refresh has its own existing validation; the login policy is not controller-wide.
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.BadRequest, refresh.StatusCode);
    }

    [Fact]
    public async Task WindowExpiry_RestoresCapacity()
    {
        using var factory = Factory(1, 1, 1);
        using var client = factory.CreateClient();
        Assert.Equal(401, (await Login(factory)).Response.StatusCode);
        Assert.Equal(429, (await Login(factory)).Response.StatusCode);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(401, (await Login(factory)).Response.StatusCode);
    }

    [Theory]
    [InlineData(0, 60, 6)]
    [InlineData(10, 0, 6)]
    [InlineData(10, 60, 0)]
    [InlineData(10, 60, 61)]
    public void InvalidConfiguration_FailsStartup(int permits, int window, int segments)
    {
        using var factory = Factory(permits, window, segments);
        Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
    }
}
