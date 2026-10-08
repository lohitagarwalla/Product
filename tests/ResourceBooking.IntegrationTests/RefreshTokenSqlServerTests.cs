using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Infrastructure.Services;

namespace ResourceBooking.IntegrationTests;

// Reuse the existing disposable SQL Server factory and ORDER_TEST_SQLSERVER_CONNECTION.
// These checks intentionally require SQL Server: InMemory cannot verify transaction locks/rollback.
public class RefreshTokenSqlServerTests(OrderWebApplicationFactory factory)
    : IClassFixture<OrderWebApplicationFactory>
{
    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

    [Fact]
    public async Task ConcurrentRefresh_IsSerialized_AndReplayRevokesWinningToken()
    {
        using var client = Client();
        var cookie = await Register(client);
        var results = await Task.WhenAll(RefreshTokenTests.SendCookie(client, "refresh", cookie),
            RefreshTokenTests.SendCookie(client, "refresh", cookie));
        var success = Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await RefreshTokenTests.SendCookie(client, "refresh", RefreshTokenTests.Cookie(success))).StatusCode);
    }

    [Fact]
    public async Task ConcurrentLogoutAndRefresh_LeaveNoRefreshableSession()
    {
        using var client = Client();
        var cookie = await Register(client);
        var results = await Task.WhenAll(RefreshTokenTests.SendCookie(client, "refresh", cookie),
            RefreshTokenTests.SendCookie(client, "logout", cookie));
        Assert.Equal(HttpStatusCode.OK, results[1].StatusCode);
        Assert.Contains(results[0].StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized });
        var latest = results[0].IsSuccessStatusCode ? RefreshTokenTests.Cookie(results[0]) : cookie;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await RefreshTokenTests.SendCookie(client, "refresh", latest)).StatusCode);
    }

    [Fact]
    public async Task FailedRotation_RollsBackConsumedTokenAndReplacement()
    {
        using var client = Client();
        var cookie = await Register(client);
        var raw = cookie[(cookie.IndexOf('=') + 1)..];
        using (var scope = factory.Services.CreateScope())
        await using (var db = factory.CreateDb(new FailAfterSave()))
        {
            var service = ActivatorUtilities.CreateInstance<AccountService>(scope.ServiceProvider, db);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.RefreshAsync(raw));
        }
        // If either write escaped the transaction, retry would detect replay or leave an extra token.
        var response = await RefreshTokenTests.SendCookie(client, "refresh", cookie);
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        await using var check = factory.CreateDb();
        var session = await check.RefreshSessions.SingleAsync(s => s.UserId == auth.UserId);
        Assert.Null(session.RevokedAt);
        Assert.Equal(2, await check.RefreshTokens.CountAsync(t => t.SessionId == session.Id));
    }

    [Fact]
    public async Task Migration_EnforcesUniqueTokenHash_AndUserForeignKey()
    {
        using var client = Client();
        await Register(client);
        await using (var db = factory.CreateDb())
        {
            Assert.False(db.Database.HasPendingModelChanges());
            var original = await db.RefreshTokens.AsNoTracking().FirstAsync();
            db.RefreshTokens.Add(new RefreshToken
            { SessionId = original.SessionId, TokenHash = original.TokenHash, CreatedAt = DateTime.UtcNow });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        await using (var db = factory.CreateDb())
        {
            db.RefreshSessions.Add(new RefreshSession
            {
                UserId = "missing-user", SecurityStamp = "test", CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    private static async Task<string> Register(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "SQL", LastName = "Refresh"
        });
        response.EnsureSuccessStatusCode();
        return RefreshTokenTests.Cookie(response);
    }

    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SQL writes, before transaction commit.");
    }
}
