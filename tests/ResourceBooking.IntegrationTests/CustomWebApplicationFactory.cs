using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class CustomWebApplicationFactory<TProgram> : WebApplicationFactory<TProgram> where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseTestJwt();
        // Shared business-test fixtures may perform many setup logins in one window.
        // LoginRateLimitTests overrides this with small limits to exercise enforcement.
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["RateLimiting:Login:PermitLimit"] = "10000" }));
        var databaseName = "IntegrationTestDb_" + Guid.NewGuid();
        builder.ConfigureServices(services =>
        {
            // 1. Remove real ApplicationDbContext registration
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();

            // 2. Add In-Memory Database for testing isolation
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseInMemoryDatabase(databaseName);
            });
        });
    }
}
