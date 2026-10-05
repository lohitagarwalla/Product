using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class AddressWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
    }
}

public class AddressEndpointsTests(AddressWebApplicationFactory factory)
    : IClassFixture<AddressWebApplicationFactory>
{
    private const string Url = "/api/profile/addresses";

    [Theory]
    [InlineData("GET", "")]
    [InlineData("GET", "/1")]
    [InlineData("POST", "")]
    [InlineData("PUT", "/1")]
    [InlineData("PUT", "/1/default")]
    [InlineData("DELETE", "/1")]
    public async Task AllOperations_RequireAuthentication(string method, string suffix)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), Url + suffix);
        if (method != "GET") request.Content = JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Lifecycle_SelectsAndReplacesDefault_AndKeepsUsersIsolated()
    {
        using var owner = await CreateUserAsync();
        using var other = await CreateUserAsync();
        Assert.Empty((await owner.GetFromJsonAsync<List<AddressResponseDto>>(Url))!);
        var first = await AddAsync(owner);
        var second = await AddAsync(owner);
        var third = await AddAsync(owner, true);
        Assert.True(first.IsDefault);
        Assert.False(second.IsDefault);
        Assert.True(third.IsDefault);
        var addresses = (await owner.GetFromJsonAsync<List<AddressResponseDto>>(Url))!;
        Assert.Equal(third.Id, addresses[0].Id);
        Assert.Single(addresses, a => a.IsDefault);
        Assert.Empty((await other.GetFromJsonAsync<List<AddressResponseDto>>(Url))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Url}/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"{Url}/{first.Id}", Update(first))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await other.PutAsJsonAsync($"{Url}/{first.Id}/default", new AddressVersionDto { RowVersion = first.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(other, first)).StatusCode);

        var response = await owner.PutAsJsonAsync($"{Url}/{second.Id}/default",
            new AddressVersionDto { RowVersion = second.RowVersion });
        response.EnsureSuccessStatusCode();
        var selected = (await response.Content.ReadFromJsonAsync<AddressResponseDto>())!;
        Assert.True(selected.IsDefault);
        // Selecting the current default again is harmless.
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync($"{Url}/{selected.Id}/default",
            new AddressVersionDto { RowVersion = selected.RowVersion })).StatusCode);
        var edited = await owner.PutAsJsonAsync($"{Url}/{selected.Id}", Update(selected));
        edited.EnsureSuccessStatusCode();
        selected = (await edited.Content.ReadFromJsonAsync<AddressResponseDto>())!;
        Assert.Equal("Updated street", selected.AddressLine1);
        Assert.True(selected.IsDefault);

        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(owner, selected)).StatusCode);
        addresses = (await owner.GetFromJsonAsync<List<AddressResponseDto>>(Url))!;
        Assert.Equal(first.Id, Assert.Single(addresses, a => a.IsDefault).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(owner, addresses.Single(a => a.Id == third.Id))).StatusCode);
        var last = (await owner.GetFromJsonAsync<List<AddressResponseDto>>(Url))!.Single();
        Assert.True(last.IsDefault);
        Assert.Equal(HttpStatusCode.NoContent, (await DeleteAsync(owner, last)).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<List<AddressResponseDto>>(Url))!);
        Assert.True((await AddAsync(owner)).IsDefault);
        Assert.Equal(HttpStatusCode.NotFound, (await DeleteAsync(owner, last)).StatusCode);
    }

    [Fact]
    public async Task Create_TrimsDetails_AndDefaultsCountryToIndia()
    {
        using var client = await CreateUserAsync();
        var dto = CreateAddress();
        dto.Label = "  Home  ";
        dto.AddressLine2 = "   ";
        dto.RecipientName = "  Address Tester  ";
        dto.PostalCode = " 560001 ";
        dto.CountryCode = "in";
        var response = await client.PostAsJsonAsync(Url, dto);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var address = (await response.Content.ReadFromJsonAsync<AddressResponseDto>())!;
        Assert.Equal("Home", address.Label);
        Assert.Null(address.AddressLine2);
        Assert.Equal("Address Tester", address.RecipientName);
        Assert.Equal("560001", address.PostalCode);
        Assert.Equal("IN", address.CountryCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(response.Headers.Location)).StatusCode);
    }

    [Theory]
    [InlineData("RecipientName", "   ")]
    [InlineData("AddressLine1", "")]
    [InlineData("City", "")]
    [InlineData("State", "")]
    [InlineData("PostalCode", "012345")]
    [InlineData("PostalCode", "56000")]
    [InlineData("PostalCode", "5600011")]
    [InlineData("PostalCode", "ABCDEF")]
    [InlineData("CountryCode", "IND")]
    [InlineData("CountryCode", "12")]
    public async Task Create_RejectsInvalidDetails(string field, string value)
    {
        using var client = await CreateUserAsync();
        var dto = CreateAddress();
        typeof(AddressWriteDto).GetProperty(field)!.SetValue(dto, value);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, dto)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<AddressResponseDto>>(Url))!);
    }

    [Fact]
    public async Task Create_RejectsExcessiveFieldLength()
    {
        using var client = await CreateUserAsync();
        var dto = CreateAddress();
        dto.AddressLine1 = new string('a', 201);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Url, dto)).StatusCode);
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("PUT", "/default")]
    [InlineData("DELETE", "")]
    public async Task Mutations_RejectStaleVersion_WithoutChangingAddresses(string method, string suffix)
    {
        using var client = await CreateUserAsync();
        var first = await AddAsync(client);
        var second = await AddAsync(client);
        // InMemory does not generate SQL rowversions. Simulate a later database version.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var saved = await db.UserAddresses.SingleAsync(a => a.Id == second.Id);
            saved.RowVersion = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
            await db.SaveChangesAsync();
        }
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Url}/{second.Id}{suffix}");
        request.Content = method == "PUT" && suffix == ""
            ? JsonContent.Create(Update(second))
            : JsonContent.Create(new AddressVersionDto { RowVersion = second.RowVersion });
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(request)).StatusCode);
        var addresses = (await client.GetFromJsonAsync<List<AddressResponseDto>>(Url))!;
        Assert.Equal(2, addresses.Count);
        Assert.Equal(first.Id, Assert.Single(addresses, a => a.IsDefault).Id);
        Assert.Equal("1 Main Street", addresses.Single(a => a.Id == second.Id).AddressLine1);
    }

    [Theory]
    [InlineData("PUT", "")]
    [InlineData("PUT", "/default")]
    [InlineData("DELETE", "")]
    public async Task Mutations_RequireValidRowVersion(string method, string suffix)
    {
        using var client = await CreateUserAsync();
        var address = await AddAsync(client);
        using var request = new HttpRequestMessage(new HttpMethod(method), $"{Url}/{address.Id}{suffix}");
        var update = Update(address);
        update.RowVersion = [];
        request.Content = method == "PUT" && suffix == "" ? JsonContent.Create(update)
            : JsonContent.Create(new AddressVersionDto());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public void SqlServerModel_DefinesFilteredUniqueDefaultIndex_AndRowVersion()
    {
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Integrated Security=true").Options);
        // Generate schema SQL without opening a connection or creating a migration.
        var sql = db.Database.GenerateCreateScript();
        Assert.Contains("CREATE UNIQUE INDEX [IX_UserAddresses_OneDefaultPerUser] ON [UserAddresses] ([UserId]) WHERE [IsDefault] = 1", sql);
        Assert.Contains("[RowVersion] rowversion NOT NULL", sql);
        Assert.Contains("FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id])", sql);
    }

    private async Task<HttpClient> CreateUserAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!",
            ConfirmPassword = "Password123!", FirstName = "Address", LastName = "Tester"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static AddressCreateDto CreateAddress(bool makeDefault = false) => new()
    {
        RecipientName = "Address Tester", AddressLine1 = "1 Main Street", City = "Bengaluru",
        State = "Karnataka", PostalCode = "560001", MakeDefault = makeDefault
    };

    private static async Task<AddressResponseDto> AddAsync(HttpClient client, bool makeDefault = false)
    {
        var response = await client.PostAsJsonAsync(Url, CreateAddress(makeDefault));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AddressResponseDto>())!;
    }

    private static AddressUpdateDto Update(AddressResponseDto a) => new()
    {
        RecipientName = a.RecipientName, AddressLine1 = "Updated street", City = a.City,
        State = a.State, PostalCode = a.PostalCode, CountryCode = a.CountryCode, RowVersion = a.RowVersion
    };

    private static async Task<HttpResponseMessage> DeleteAsync(HttpClient client, AddressResponseDto a)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Url}/{a.Id}")
        {
            Content = JsonContent.Create(new AddressVersionDto { RowVersion = a.RowVersion })
        };
        return await client.SendAsync(request);
    }
}
