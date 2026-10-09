using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ResourceBooking.IntegrationTests;

internal static class TestJwtConfiguration
{
    public static void UseTestJwt(this IWebHostBuilder builder)
    {
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = secret,
                ["JwtSettings:Issuer"] = "ResourceBookingTests",
                ["JwtSettings:Audience"] = "ResourceBookingTestClients"
            }));
    }
}
