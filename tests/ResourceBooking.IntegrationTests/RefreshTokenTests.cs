using ResourceBooking.Core.Constants;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Web.Controllers;

namespace ResourceBooking.IntegrationTests;

public class RefreshTokenTests : IDisposable
{
    private readonly RefreshTestFactory factory = new();
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), HandleCookies = false
    });

    [Fact]
    public async Task Registration_IssuesSecureCookie_AndStoresOnlyHash()
    {
        using var client = Client();
        var (auth, cookie, _) = await Register(client);
        Assert.True(auth.IsSuccess);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.Token);
        Assert.InRange(jwt.ValidTo, factory.Clock.Now.UtcDateTime.AddMinutes(15).AddSeconds(-2),
            factory.Clock.Now.UtcDateTime.AddMinutes(15));
        Assert.Equal(factory.Clock.Now.UtcDateTime.AddDays(7), auth.RefreshTokenExpiresAt);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var token = await db.RefreshTokens.Include(t => t.Session).SingleAsync();
        var raw = cookie[(cookie.IndexOf('=') + 1)..];
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), token.TokenHash);
        Assert.NotEqual(raw, token.TokenHash);
        Assert.Equal(auth.UserId, token.Session.UserId);
    }

    [Fact]
    public async Task Refresh_RotatesCookie_KeepsAbsoluteExpiry_AndReturnsCurrentRoles()
    {
        using var client = Client();
        var (original, cookie, _) = await Register(client);
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(original.UserId))!;
            Assert.True((await users.AddToRoleAsync(user, Roles.Admin)).Succeeded);
        }
        factory.Clock.Now += TimeSpan.FromHours(1);
        var response = await SendCookie(client, "refresh", cookie);
        response.EnsureSuccessStatusCode();
        var rotated = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        Assert.NotEqual(cookie, Cookie(response));
        Assert.NotEqual(original.Token, rotated.Token);
        Assert.Equal(original.RefreshTokenExpiresAt, rotated.RefreshTokenExpiresAt);
        Assert.Contains(Roles.Admin, rotated.Roles);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(rotated.Token).Claims,
            c => c.Value == Roles.Admin);
        using var scope2 = factory.Services.CreateScope();
        var db = scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UsedAt != null));
    }

    [Fact]
    public async Task ExpiredAccessToken_CanRefresh_AndNewTokenCallsProtectedApi()
    {
        factory.Clock.Now -= TimeSpan.FromHours(1);
        using var client = Client();
        var (auth, cookie, _) = await Register(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/todos")).StatusCode);
        factory.Clock.Now += TimeSpan.FromHours(1);
        var refresh = await SendCookie(client, "refresh", cookie);
        refresh.EnsureSuccessStatusCode();
        var renewed = (await refresh.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", renewed.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/todos")).StatusCode);
    }

    [Fact]
    public async Task Replay_RevokesDescendant_ButNotAnotherLogin()
    {
        using var client = Client();
        var (_, first, registration) = await Register(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        { Email = registration.Email, Password = registration.Password });
        login.EnsureSuccessStatusCode();
        var independent = Cookie(login);
        var rotated = await SendCookie(client, "refresh", first);
        rotated.EnsureSuccessStatusCode();
        var next = await SendCookie(client, "refresh", Cookie(rotated));
        next.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendCookie(client, "refresh", first)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendCookie(client, "refresh", Cookie(next))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendCookie(client, "refresh", independent)).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRefresh_OnlyOneSucceeds_AndReplayRevokesSession()
    {
        using var client = Client();
        var (_, cookie, _) = await Register(client);
        var responses = await Task.WhenAll(SendCookie(client, "refresh", cookie), SendCookie(client, "refresh", cookie));
        var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendCookie(client, "refresh", Cookie(success))).StatusCode);
    }

    [Fact]
    public async Task Logout_WithExpiredAccessToken_RevokesOnlyItsSession_AndClearsCookie()
    {
        factory.Clock.Now -= TimeSpan.FromHours(1);
        using var client = Client();
        var (auth, cookie, registration) = await Register(client);
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        { Email = registration.Email, Password = registration.Password });
        login.EnsureSuccessStatusCode();
        var other = Cookie(login);
        factory.Clock.Now += TimeSpan.FromHours(1);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var logout = await SendCookie(client, "logout", cookie);
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", logout.Headers.GetValues("Set-Cookie").Single());
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendCookie(client, "refresh", cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendCookie(client, "refresh", other)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendCookie(client, "logout", cookie)).StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("stamp")]
    [InlineData("locked")]
    [InlineData("deleted")]
    public async Task Refresh_RejectsInvalidSessionOrUser(string reason)
    {
        using var client = Client();
        var (auth, cookie, _) = await Register(client);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.SingleAsync(u => u.Id == auth.UserId);
            if (reason == "expired") factory.Clock.Now += TimeSpan.FromDays(7);
            if (reason == "stamp") user.SecurityStamp = Guid.NewGuid().ToString();
            if (reason == "locked") { user.LockoutEnabled = true; user.LockoutEnd = DateTimeOffset.UtcNow.AddDays(1); }
            if (reason == "deleted") db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
        var response = await SendCookie(client, "refresh", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", response.Headers.GetValues("Set-Cookie").Single());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformed")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Refresh_MissingOrUnknownToken_Returns401(string? rawToken)
    {
        using var client = Client();
        var cookie = rawToken is null ? null : $"{AuthController.RefreshCookieName}={rawToken}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendCookie(client, "refresh", cookie)).StatusCode);
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("logout")]
    public async Task CookieEndpoints_RejectMissingHeaderAndUntrustedOrigins_WithoutChangingSession(string endpoint)
    {
        using var client = Client();
        var (_, cookie, _) = await Register(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendCookie(client, endpoint, cookie, header: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendCookie(client, endpoint, cookie, origin: "https://evil.example")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendCookie(client, endpoint, cookie, origin: "null")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendCookie(client, "refresh", cookie, origin: "http://localhost:5173")).StatusCode);
    }

    [Fact]
    public async Task Cors_AllowsCredentialsOnlyForConfiguredOrigin()
    {
        using var client = Client();
        foreach (var origin in new[] { "http://localhost:5173", "https://evil.example" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "X-Refresh-Token");
            var response = await client.SendAsync(request);
            if (origin == "http://localhost:5173")
            {
                Assert.Equal(origin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
                Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
            }
            else Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    [Fact]
    public async Task Login_RejectsBadPasswordAndUntrustedOrigin_WithoutCreatingSession()
    {
        using var client = Client();
        var (_, _, registration) = await Register(client);
        var bad = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        { Email = registration.Email, Password = "WrongPassword123!" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        Assert.False(bad.Headers.Contains("Set-Cookie"));
        client.DefaultRequestHeaders.Add("Origin", "https://evil.example");
        var evil = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        { Email = registration.Email, Password = registration.Password });
        Assert.Equal(HttpStatusCode.Forbidden, evil.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().RefreshSessions.CountAsync());
    }

    private static async Task<(AuthResponseDto Auth, string Cookie, RegisterDto Registration)> Register(HttpClient client)
    {
        var dto = new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "Refresh", LastName = "Tester"
        };
        var response = await client.PostAsJsonAsync("/api/auth/register", dto);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        Assert.False(JsonDocument.Parse(json).RootElement.TryGetProperty("refreshToken", out _));
        Assert.True(response.Headers.CacheControl!.NoStore);
        var setCookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.Contains("httponly", setCookie);
        Assert.Contains("secure", setCookie);
        Assert.Contains("samesite=none", setCookie);
        Assert.Contains("path=/api/auth", setCookie);
        Assert.DoesNotContain("domain=", setCookie);
        return ((await response.Content.ReadFromJsonAsync<AuthResponseDto>())!, Cookie(response), dto);
    }

    internal static string Cookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single().Split(';')[0];

    internal static Task<HttpResponseMessage> SendCookie(HttpClient client, string endpoint, string? cookie,
        bool header = true, string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/{endpoint}");
        if (cookie is not null) request.Headers.Add("Cookie", cookie);
        if (header) request.Headers.Add("X-Refresh-Token", "1");
        if (origin is not null) request.Headers.Add("Origin", origin);
        return client.SendAsync(request);
    }

    public void Dispose() => factory.Dispose();
}

public class RefreshTestFactory : CustomWebApplicationFactory<Program>
{
    public TestAuthClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

public class TestAuthClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}
