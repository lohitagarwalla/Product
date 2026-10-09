using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ResourceBooking.Core.DTOs;

namespace ResourceBooking.IntegrationTests;

public class JwtConfigurationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("                                ")]
    [InlineData("short-secret")]
    [InlineData("1234567890123456789012345678901")]
    public void Startup_RejectsInvalidSecret_BeforeDatabaseAccess(string? secret)
    {
        // Deliberately keep the real database registration: invalid JWT settings must fail first.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["JwtSettings:Secret"] = secret }));
        });
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("JwtSettings:Secret", error.Message);
        if (!string.IsNullOrWhiteSpace(secret)) Assert.DoesNotContain(secret, error.Message);
    }

    [Theory]
    [InlineData("Issuer")]
    [InlineData("Audience")]
    public void Startup_RejectsBlankTokenIdentifiers(string setting)
    {
        using var factory = new CustomWebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { [$"JwtSettings:{setting}"] = " " })));
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains($"JwtSettings:{setting}", error.Message);
    }

    [Fact]
    public async Task ConfiguredKey_SignsAndValidatesTokens_AndRejectsAnotherKey()
    {
        using var factory = new CustomWebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost") });
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!",
            ConfirmPassword = "Password123!", FirstName = "JWT", LastName = "Test"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        var settings = factory.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        var handler = new JwtSecurityTokenHandler();
        handler.ValidateToken(auth.Token, new TokenValidationParameters
        {
            ValidIssuer = settings.Issuer, ValidAudience = settings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Secret)),
            ValidateIssuerSigningKey = true, ValidateLifetime = true
        }, out _);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/todos")).StatusCode);

        var original = handler.ReadJwtToken(auth.Token);
        var forged = new JwtSecurityToken(settings.Issuer, settings.Audience, original.Claims,
            expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", handler.WriteToken(forged));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/todos")).StatusCode);
    }
}
