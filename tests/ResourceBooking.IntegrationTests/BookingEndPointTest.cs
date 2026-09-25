using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using ResourceBooking.Core.DTOs;
using Xunit;

namespace ResourceBooking.IntegrationTests;

public class BookingEndpointsTests : IClassFixture<CustomWebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public BookingEndpointsTests(CustomWebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAllResources_ReturnsSuccessAndJsonContent()
    {
        // Act
        var response = await _client.GetAsync("/api/resources");

        // Assert
        response.EnsureSuccessStatusCode(); // Verifies 200 OK
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var resources = await response.Content.ReadFromJsonAsync<List<ResourceResponseDto>>();
        Assert.NotNull(resources);
        Assert.NotEmpty(resources); // Seeded resources should be returned
    }

    [Fact]
    public async Task CreateBooking_WithInvalidDates_ReturnsBadRequest400()
    {
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto
        {
            Email = "admin@company.com", Password = "Admin123!"
        });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponseDto>();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);
        // Arrange: EndTime is before StartTime (triggers DateGreaterThan validation attribute)
        var invalidBooking = new BookingCreateDto
        {
            ResourceId = 1,
            StartTime = DateTime.UtcNow.AddHours(5),
            EndTime = DateTime.UtcNow.AddHours(2), // Invalid!
            Purpose = "Team Sync Meeting"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/bookings", invalidBooking);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
