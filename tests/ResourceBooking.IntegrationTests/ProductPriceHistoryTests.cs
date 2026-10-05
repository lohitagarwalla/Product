using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Services;
using ResourceBooking.Infrastructure.Storage;

namespace ResourceBooking.IntegrationTests;

// Reuse the disposable SQL Server factory to verify transactions, constraints, and migrations.
public class ProductPriceHistoryTests(OrderWebApplicationFactory factory) : IClassFixture<OrderWebApplicationFactory>
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

    private static ProductWriteDto Write(decimal price = 100m, string currency = "INR") => new()
    { Title = "History product", Brand = "Example", Price = price, Currency = currency };

    private static async Task<ProductResponseDto> CreateAsync(HttpClient admin, decimal price = 100m)
    {
        var response = await admin.PostAsJsonAsync("/api/products", Write(price));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    private static async Task<ProductPriceHistoryPageDto> HistoryAsync(HttpClient admin, int id, string query = "")
    {
        var response = await admin.GetAsync($"/api/products/{id}/price-history{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductPriceHistoryPageDto>())!;
    }

    private static ProductService Service(ApplicationDbContext db) =>
        new(db, null!, Options.Create(new ImageStorageOptions()));

    [Fact]
    public async Task CreateAndPriceChanges_RecordExactValuesActorAndNewestFirst()
    {
        using var admin = await AdminAsync();
        var start = DateTime.UtcNow;
        var product = await CreateAsync(admin, 9999999999999999.99m);
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", Write(0m))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", Write(123.45m))).EnsureSuccessStatusCode();
        var history = await HistoryAsync(admin, product.Id);
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(new[] { 123.45m, 0m, 9999999999999999.99m }, history.Items.Select(h => h.NewPrice));
        Assert.Equal(0m, history.Items[0].PreviousPrice);
        Assert.Equal(9999999999999999.99m, history.Items[1].PreviousPrice);
        Assert.Null(history.Items[2].PreviousPrice);
        Assert.Equal(new[] { "PriceChanged", "PriceChanged", "Created" }, history.Items.Select(h => h.EntryType));
        await using var db = factory.CreateDb();
        var actor = await db.Users.Where(u => u.Email == "admin@company.com").Select(u => u.Id).SingleAsync();
        Assert.All(history.Items, h =>
        {
            Assert.Equal(actor, h.ChangedByUserId);
            Assert.Equal(product.Id, h.ProductId);
            Assert.InRange(h.ChangedAtUtc, start, DateTime.UtcNow);
        });
    }

    [Fact]
    public async Task SamePriceCurrencyAndOtherEdits_DoNotAddHistory()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        var write = Write(100m, "USD");
        write.Title = "New title";
        write.Description = "New description";
        write.IsPublished = true;
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", write)).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", write)).EnsureSuccessStatusCode();
        Assert.Single((await HistoryAsync(admin, product.Id)).Items);
        var current = await admin.GetFromJsonAsync<ProductResponseDto>($"/api/products/{product.Id}");
        Assert.Equal("USD", current!.Currency);
        Assert.Equal("New title", current.Title);
    }

    [Fact]
    public async Task History_IsAdminOnlyAndValidatesPaginationAndMissingProducts()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/products/{product.Id}/price-history")).StatusCode);
        var register = await anonymous.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "History", LastName = "Viewer"
        });
        register.EnsureSuccessStatusCode();
        var auth = (await register.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        anonymous.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync($"/api/products/{product.Id}/price-history")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/products/{product.Id}/price-history?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/products/{product.Id}/price-history?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync("/api/products/2147483647/price-history")).StatusCode);
    }

    [Fact]
    public async Task History_RemainsAfterSoftDeletionAndPaginatesWithTimestampTies()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        await using (var db = factory.CreateDb())
        {
            var actor = await db.Users.Select(u => u.Id).FirstAsync();
            var timestamp = DateTime.UtcNow.AddMinutes(1);
            db.ProductPriceHistory.AddRange(Enumerable.Range(1, 3).Select(i => new ProductPriceHistory
            {
                ProductId = product.Id, PreviousPrice = i, NewPrice = i + 1,
                ChangedByUserId = actor, ChangedAtUtc = timestamp, EntryType = ProductPriceHistoryEntryType.PriceChanged
            }));
            await db.SaveChangesAsync();
        }
        (await admin.DeleteAsync($"/api/products/{product.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/products/{product.Id}")).StatusCode);
        var first = await HistoryAsync(admin, product.Id, "?page=1&pageSize=2");
        var second = await HistoryAsync(admin, product.Id, "?page=2&pageSize=2");
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(2, second.Items.Count);
        Assert.Equal(new[] { 4m, 3m, 2m, 100m }, first.Items.Concat(second.Items).Select(h => h.NewPrice));
        Assert.Equal(4, first.Items.Concat(second.Items).Select(h => h.Id).Distinct().Count());
    }

    [Fact]
    public async Task ConcurrentPriceChanges_RollBackLosingUpdateAndItsHistory()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        await using var first = factory.CreateDb();
        await using var second = factory.CreateDb();
        await first.Products.SingleAsync(p => p.Id == product.Id);
        await second.Products.SingleAsync(p => p.Id == product.Id);
        var actor = await first.Users.Select(u => u.Id).FirstAsync();
        await Service(first).UpdateAsync(product.Id, Write(200m), actor, default);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            Service(second).UpdateAsync(product.Id, Write(300m), actor, default));
        await using var verify = factory.CreateDb();
        Assert.Equal(200m, (await verify.Products.SingleAsync(p => p.Id == product.Id)).Price);
        var history = await verify.ProductPriceHistory.Where(h => h.ProductId == product.Id).ToListAsync();
        Assert.Equal(2, history.Count);
        Assert.DoesNotContain(history, h => h.NewPrice == 300m);
    }

    [Fact]
    public async Task FailedHistoryInsert_RollsBackPriceUpdateAndProductCreation()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        var missingActor = Guid.NewGuid().ToString();
        await using (var db = factory.CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).UpdateAsync(product.Id, Write(200m), missingActor, default));
        var write = Write();
        write.Title = Guid.NewGuid().ToString();
        await using (var db = factory.CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => Service(db).CreateAsync(write, missingActor, default));
        await using var verify = factory.CreateDb();
        Assert.Equal(100m, (await verify.Products.SingleAsync(p => p.Id == product.Id)).Price);
        Assert.Single(await verify.ProductPriceHistory.Where(h => h.ProductId == product.Id).ToListAsync());
        Assert.False(await verify.Products.AnyAsync(p => p.Title == write.Title));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task History_IsAppendOnly(bool delete, bool asyncSave)
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        await using var db = factory.CreateDb();
        var entry = await db.ProductPriceHistory.SingleAsync(h => h.ProductId == product.Id);
        if (delete) db.ProductPriceHistory.Remove(entry);
        else entry.NewPrice = 200m;
        if (asyncSave) await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        else Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task DatabaseConstraints_RejectNegativePricesAndUnchangedPriceEntries()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        await using var db = factory.CreateDb();
        var actor = await db.Users.Select(u => u.Id).FirstAsync();
        foreach (var price in new[] { -1m, 100m })
        {
            db.ProductPriceHistory.Add(new ProductPriceHistory
            {
                ProductId = product.Id, PreviousPrice = 100m, NewPrice = price, ChangedByUserId = actor,
                ChangedAtUtc = DateTime.UtcNow, EntryType = ProductPriceHistoryEntryType.PriceChanged
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
        }
    }

    [Fact]
    public async Task Migration_BackfillsCurrentPricesIncludingDeletedProductsWithoutInventingPastValues()
    {
        var databaseName = $"ResourceBookingPriceMigrationTests_{Guid.NewGuid():N}";
        var connection = new SqlConnectionStringBuilder(factory.ConnectionString) { InitialCatalog = databaseName };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection.ConnectionString).Options;
        await using var db = new ApplicationDbContext(options);
        try
        {
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261001134859_AddOrdersAndStatusHistory");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO [Products] ([IsDeleted], [Title], [Description], [Brand], [Price], [Currency], [IsPublished], [Version], [CreatedAt])
                VALUES (0, 'Existing active', '', 'Example', 123.45, 'INR', 1, NEWID(), SYSUTCDATETIME()),
                       (1, 'Existing deleted', '', 'Example', 987.65, 'INR', 0, NEWID(), SYSUTCDATETIME());
                """);
            var start = DateTime.UtcNow;
            await migrator.MigrateAsync();
            var history = await db.ProductPriceHistory.OrderBy(h => h.NewPrice).ToListAsync();
            Assert.Equal(new[] { 123.45m, 987.65m }, history.Select(h => h.NewPrice));
            Assert.All(history, h =>
            {
                Assert.Null(h.PreviousPrice);
                Assert.Null(h.ChangedByUserId);
                Assert.Equal(ProductPriceHistoryEntryType.Baseline, h.EntryType);
                Assert.InRange(h.ChangedAtUtc, start, DateTime.UtcNow);
            });
            await migrator.MigrateAsync();
            Assert.Equal(2, await db.ProductPriceHistory.CountAsync());
        }
        finally
        {
            if (connection.InitialCatalog != databaseName || !databaseName.StartsWith("ResourceBookingPriceMigrationTests_", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected test database name.");
            await db.Database.EnsureDeletedAsync();
        }
    }
}
