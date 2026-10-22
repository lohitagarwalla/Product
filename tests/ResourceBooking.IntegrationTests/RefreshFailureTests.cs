using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Exceptions;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class RefreshFailureTests
{
    [Fact]
    public async Task TransientFailureDuringRotation_IsNotRetriedOrReportedAsWakeupFailure()
    {
        var interceptor = new FailRotationSave();
        using var factory = new RefreshTestFactory();
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<ApplicationDbContext>(options => options.AddInterceptors(interceptor))));
        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });
        using var registration = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "Retry", LastName = "Boundary"
        });
        registration.EnsureSuccessStatusCode();
        interceptor.Armed = true;

        using var response = await RefreshTokenTests.SendCookie(client, "refresh", RefreshTokenTests.Cookie(registration));

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(1, interceptor.Attempts);
        Assert.False(response.Headers.Contains("Set-Cookie"));
    }

    private sealed class FailRotationSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!Armed) return ValueTask.FromResult(result);
            Attempts++;
            throw new InvalidOperationException("EF wrapper", DatabaseConnectionRetryTests.SqlFailure(40613));
        }
    }

    [Theory]
    [InlineData("direct", HttpStatusCode.InternalServerError)]
    [InlineData("wrapped", HttpStatusCode.InternalServerError)]
    [InlineData("nested", HttpStatusCode.InternalServerError)]
    [InlineData("update", HttpStatusCode.InternalServerError)]
    [InlineData("validation", HttpStatusCode.BadRequest)]
    [InlineData("unavailable", HttpStatusCode.ServiceUnavailable)]
    public async Task Refresh_ClassifiesFailures_WithoutDeletingCookie(string failure, HttpStatusCode expected)
    {
        Exception exception = failure switch
        {
            "unavailable" => new DatabaseUnavailableException(new Exception("private SQL diagnostic")),
            "direct" => new UnavailableDatabaseException(),
            "wrapped" => new InvalidOperationException("Transient database failure", new UnavailableDatabaseException()),
            "nested" => new InvalidOperationException("Outer failure",
                new InvalidOperationException("Transient database failure", new UnavailableDatabaseException())),
            "update" => new InvalidOperationException("Save failed", new DbUpdateException("Database failure")),
            _ => new InvalidOperationException("Business validation failed")
        };
        using var factory = new RefreshTestFactory();
        using var failingFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAccountService>();
            services.AddScoped<IAccountService>(_ => new FailingAccountService(exception));
        }));
        using var client = failingFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"), HandleCookies = false
        });

        using var response = await RefreshTokenTests.SendCookie(client, "refresh", "__Secure-ResourceBooking.Refresh=test");

        Assert.Equal(expected, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal((int)expected, problem.Status);
        Assert.Equal("/api/auth/refresh", problem.Instance);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        if (expected == HttpStatusCode.ServiceUnavailable)
            Assert.DoesNotContain("private SQL diagnostic", await response.Content.ReadAsStringAsync());
    }

    private sealed class UnavailableDatabaseException : DbException { }

    private sealed class FailingAccountService(Exception exception) : IAccountService
    {
        public Task<AuthResponseDto> RefreshAsync(string? refreshToken, CancellationToken ct = default) =>
            Task.FromException<AuthResponseDto>(exception);

        public Task<AuthResponseDto> RegisterAsync(RegisterDto dto) => throw new NotSupportedException();
        public Task<AuthResponseDto> LoginAsync(LoginDto dto) => throw new NotSupportedException();
        public Task LogoutAsync(string? refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string> GenerateJwtTokenAsync(ApplicationUser user) => throw new NotSupportedException();
    }
}
