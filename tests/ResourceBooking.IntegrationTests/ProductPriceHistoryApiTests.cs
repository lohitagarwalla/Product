using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

// API behavior can also run without SQL Server. Transaction/constraint tests live in ProductPriceHistoryTests.
public class ProductPriceHistoryApiTests(ProductWebApplicationFactory factory) : IClassFixture<ProductWebApplicationFactory>
{
    private async Task<HttpClient> AdminAsync()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginDto
        { Email = "admin@company.com", Password = "Admin123!" });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static ProductWriteDto Write(decimal price = 100m) => new()
    { Title = "History API product", Brand = "Example", Price = price };

    private static async Task<int> CreateAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/products", Write());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!.Id;
    }

    [Fact]
    public async Task PriceChanges_RecordActorAndValues_AndSurviveDeletionWithPagination()
    {
        using var admin = await AdminAsync();
        var start = DateTime.UtcNow;
        var id = await CreateAsync(admin);
        (await admin.PutAsJsonAsync($"/api/products/{id}", Write(200.25m))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{id}", Write(0m))).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/products/{id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/products/{id}")).StatusCode);
        var first = (await admin.GetFromJsonAsync<ProductPriceHistoryPageDto>($"/api/products/{id}/price-history?pageSize=2"))!;
        var second = (await admin.GetFromJsonAsync<ProductPriceHistoryPageDto>($"/api/products/{id}/price-history?page=2&pageSize=2"))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Single(second.Items);
        var entries = first.Items.Concat(second.Items).ToList();
        Assert.Equal(new[] { 0m, 200.25m, 100m }, entries.Select(h => h.NewPrice));
        Assert.Equal(new decimal?[] { 200.25m, 100m, null }, entries.Select(h => h.PreviousPrice));
        Assert.Equal(new[] { "PriceChanged", "PriceChanged", "Created" }, entries.Select(h => h.EntryType));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var actor = await db.Users.Where(u => u.Email == "admin@company.com").Select(u => u.Id).SingleAsync();
        Assert.All(entries, h =>
        {
            Assert.Equal(id, h.ProductId);
            Assert.Equal(actor, h.ChangedByUserId);
            Assert.InRange(h.ChangedAtUtc, start, DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task CurrencyAndOtherEditsAndInvalidPrices_DoNotAddPriceHistory()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        var write = Write();
        write.Currency = "USD";
        write.Title = "Changed title";
        write.IsPublished = true;
        (await admin.PutAsJsonAsync($"/api/products/{id}", write)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{id}", write)).EnsureSuccessStatusCode();
        write.Price = 1.234m;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/products/{id}", write)).StatusCode);
        write.Price = -1m;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/products/{id}", write)).StatusCode);
        var history = (await admin.GetFromJsonAsync<ProductPriceHistoryPageDto>($"/api/products/{id}/price-history"))!;
        Assert.Single(history.Items);
        var product = (await admin.GetFromJsonAsync<ProductResponseDto>($"/api/products/{id}"))!;
        Assert.Equal(100m, product.Price);
        Assert.Equal("USD", product.Currency);
        Assert.Equal("Changed title", product.Title);
    }

    [Fact]
    public async Task History_RequiresAdminAndValidPagination()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/products/{id}/price-history")).StatusCode);
        var register = await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "History", LastName = "Viewer"
        });
        register.EnsureSuccessStatusCode();
        var auth = (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/products/{id}/price-history")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/products/{id}/price-history?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/products/{id}/price-history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/products/2147483647/price-history")).StatusCode);
    }

    [Theory]
    [InlineData("Ada", "Lovelace", "ada@example.test", "Ada Lovelace")]
    [InlineData("", "", "account@example.test", "account@example.test")]
    public async Task History_ResolvesActorNameAndKeepsUnknownBaselineActorNull(
        string firstName, string lastName, string userName, string expectedName)
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        var actorId = Guid.NewGuid().ToString();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Users.Add(new ApplicationUser
            {
                Id = actorId, UserName = userName, FirstName = firstName, LastName = lastName
            });
            db.ProductPriceHistory.AddRange(
                new ProductPriceHistory
                {
                    ProductId = id, PreviousPrice = 100m, NewPrice = 150m,
                    ChangedByUserId = actorId, ChangedAtUtc = DateTime.UtcNow,
                    EntryType = ProductPriceHistoryEntryType.PriceChanged
                },
                new ProductPriceHistory
                {
                    ProductId = id, NewPrice = 100m, ChangedAtUtc = DateTime.UtcNow,
                    EntryType = ProductPriceHistoryEntryType.Baseline
                });
            await db.SaveChangesAsync();
        }
        var history = (await admin.GetFromJsonAsync<ProductPriceHistoryPageDto>($"/api/products/{id}/price-history"))!;
        var actorEntry = Assert.Single(history.Items, h => h.ChangedByUserId == actorId);
        Assert.Equal(expectedName, actorEntry.ChangedByUserName);
        var baseline = Assert.Single(history.Items, h => h.EntryType == "Baseline");
        Assert.Null(baseline.ChangedByUserId);
        Assert.Null(baseline.ChangedByUserName);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task History_RejectsModificationAndDeletion(bool delete, bool asyncSave)
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var entry = await db.ProductPriceHistory.SingleAsync(h => h.ProductId == id);
        if (delete) db.ProductPriceHistory.Remove(entry);
        else entry.NewPrice = 200m;
        if (asyncSave) await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        else Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }
}
