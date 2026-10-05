using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class RegistrationPhoneNumberTests(CustomWebApplicationFactory<Program> factory)
    : IClassFixture<CustomWebApplicationFactory<Program>>
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("6123456789", "+916123456789")]
    [InlineData("7123456789", "+917123456789")]
    [InlineData("8123456789", "+918123456789")]
    [InlineData("9123456789", "+919123456789")]
    [InlineData("+919123456789", "+919123456789")]
    public async Task Register_AcceptsOptionalOrValidPhone_AndStoresUnverifiedNumber(
        string? phoneNumber, string? expectedPhoneNumber)
    {
        using var client = factory.CreateClient();
        var dto = CreateRegistration(phoneNumber);
        var response = await client.PostAsJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>();
        Assert.True(auth!.IsSuccess);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == dto.Email);
        Assert.Equal(expectedPhoneNumber, user.PhoneNumber);
        Assert.False(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task Register_AcceptsOmittedPhoneNumber()
    {
        using var client = factory.CreateClient();
        var dto = CreateRegistration(null);
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            dto.Email, dto.Password, dto.ConfirmPassword, dto.FirstName, dto.LastName
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == dto.Email);
        Assert.Null(user.PhoneNumber);
    }

    [Theory]
    [InlineData("5123456789")]
    [InlineData("0123456789")]
    [InlineData("912345678")]
    [InlineData("91234567890")]
    [InlineData("+449123456789")]
    [InlineData("919123456789")]
    [InlineData("+91 9123456789")]
    [InlineData("91234-56789")]
    [InlineData(" 9123456789 ")]
    [InlineData("   ")]
    [InlineData("912345678a")]
    [InlineData("9१२३४५६७८९")]
    [InlineData("9123456789\n")]
    public async Task Register_RejectsInvalidPhone_BeforeCreatingUser(string phoneNumber)
    {
        using var client = factory.CreateClient();
        var dto = CreateRegistration(phoneNumber);
        var response = await client.PostAsJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>();
        Assert.Contains(nameof(RegisterDto.PhoneNumber), problem!.Errors.Keys);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == dto.Email));
    }

    private static RegisterDto CreateRegistration(string? phoneNumber) => new()
    {
        Email = $"{Guid.NewGuid():N}@example.com",
        Password = "Password123!",
        ConfirmPassword = "Password123!",
        FirstName = "Phone",
        LastName = "Tester",
        PhoneNumber = phoneNumber
    };
}
