using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Services;
using ResourceBooking.Infrastructure.Storage;
using SkiaSharp;

namespace ResourceBooking.IntegrationTests;

public class ProductWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    public string ImageRoot { get; } = Path.Combine(Path.GetTempPath(), "ResourceBookingImageTests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services => services.PostConfigure<ImageStorageOptions>(options =>
        {
            options.RootPath = ImageRoot;
            options.CleanupEnabled = false;
            options.MaxImagesPerProduct = 3;
            options.MaxFileSizeBytes = 1024 * 1024;
        }));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ResourceBookingImageTests")) + Path.DirectorySeparatorChar;
        if (Path.GetFullPath(ImageRoot).StartsWith(parent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(ImageRoot))
            Directory.Delete(ImageRoot, true);
    }
}

public class ProductEndpointsTests(ProductWebApplicationFactory factory) : IClassFixture<ProductWebApplicationFactory>
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

    private static ProductWriteDto Product(bool published = false) => new()
    { Title = "  Test phone  ", Description = "Description", Brand = "Example", Price = 199.99m, IsPublished = published };

    private static async Task<ProductResponseDto> CreateAsync(HttpClient admin, bool published = false)
    {
        var result = await admin.PostAsJsonAsync("/api/products", Product(published));
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        Assert.NotNull(result.Headers.Location);
        return (await result.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    private static byte[] ImageBytes(SKEncodedImageFormat format = SKEncodedImageFormat.Png, int width = 2)
    {
        using var bitmap = new SKBitmap(width, 2);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, 90);
        return encoded.ToArray();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, int id, byte[]? bytes = null)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? ImageBytes());
        // Intentionally misleading metadata: server must inspect the file itself.
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "photo.png");
        form.Add(new StringContent("Front view"), "altText");
        return await client.PostAsync($"/api/products/{id}/images", form);
    }

    private static async Task<ProductImageResponseDto> AddImageAsync(HttpClient client, int id)
    {
        var response = await UploadAsync(client, id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductImageResponseDto>())!;
    }

    [Fact]
    public async Task Lifecycle_DraftPublishGalleryDownloadAndDelete()
    {
        using var admin = await AdminAsync();
        using var anonymous = factory.CreateClient();
        var product = await CreateAsync(admin);
        Assert.Equal("Test phone", product.Title);
        Assert.False(product.IsPublished);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/products/{product.Id}")).StatusCode);
        var first = await AddImageAsync(admin, product.Id);
        var second = await AddImageAsync(admin, product.Id);
        Assert.Equal("image/png", first.ContentType);
        Assert.Equal(2, first.Width);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(first.Url)).StatusCode);

        (await admin.PutAsJsonAsync($"/api/products/{product.Id}/images/order", new { imageIds = new[] { second.Id, first.Id } })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}/images/{second.Id}", new { altText = "New cover" })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", Product(true))).EnsureSuccessStatusCode();
        var published = (await anonymous.GetFromJsonAsync<ProductResponseDto>($"/api/products/{product.Id}"))!;
        Assert.Equal(second.Id, published.Images[0].Id);
        Assert.Equal("New cover", published.Images[0].AltText);
        Assert.Equal(new[] { 0, 1 }, published.Images.Select(i => i.SortOrder));
        var content = await anonymous.GetAsync(first.Url);
        Assert.Equal("image/png", content.Content.Headers.ContentType!.MediaType);
        Assert.Equal(ImageBytes(), await content.Content.ReadAsByteArrayAsync());
        var download = await anonymous.GetAsync(first.DownloadUrl);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);

        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", Product(false))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/products/{product.Id}")).StatusCode);
        (await admin.PutAsJsonAsync($"/api/products/{product.Id}", Product(true))).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}/images/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(first.Url)).StatusCode);
        await CleanupAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.ImageAssets.AnyAsync(i => i.Id == first.Id));
        }
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/products/{product.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(second.Url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/products/{product.Id}")).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True((await db.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == product.Id)).IsDeleted);
            Assert.True(await db.ProductImages.IgnoreQueryFilters().AnyAsync(i => i.ProductId == product.Id));
            Assert.True((await db.ImageAssets.FindAsync(second.Id))!.DeleteAfterUtc > DateTime.UtcNow.AddDays(29));
        }
    }

    [Fact]
    public async Task Catalog_PaginatesAndHidesDrafts_ManageIncludesDrafts()
    {
        using var admin = await AdminAsync();
        using var anonymous = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N");
        foreach (var published in new[] { false, true, true })
        {
            var dto = Product(published); dto.Title = unique;
            (await admin.PostAsJsonAsync("/api/products", dto)).EnsureSuccessStatusCode();
        }
        var page = (await anonymous.GetFromJsonAsync<ProductPageDto>($"/api/products?search={unique}&pageSize=1&page=2"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Items);
        var manage = (await admin.GetFromJsonAsync<ProductPageDto>($"/api/products/manage?search={unique}"))!;
        Assert.Equal(3, manage.TotalCount);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/products/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/products?pageSize=101")).StatusCode);
    }

    [Fact]
    public async Task Writes_RequireAdmin()
    {
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/products", Product())).StatusCode);
        using var employee = factory.CreateClient();
        var registration = await employee.PostAsJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
            FirstName = "Product", LastName = "Reader"
        });
        registration.EnsureSuccessStatusCode();
        var auth = (await registration.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        employee.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.PostAsJsonAsync("/api/products", Product())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await employee.DeleteAsync("/api/products/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(employee, 1)).StatusCode);
    }

    [Theory]
    [InlineData("", 1, "INR")]
    [InlineData("   ", 1, "INR")]
    [InlineData("Phone", -1, "INR")]
    [InlineData("Phone", 1.234, "INR")]
    [InlineData("Phone", 1, "RUPEES")]
    public async Task InvalidProduct_ReturnsBadRequest(string title, decimal price, string currency)
    {
        using var admin = await AdminAsync();
        var dto = Product(); dto.Title = title; dto.Price = price; dto.Currency = currency;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/products", dto)).StatusCode);
    }

    [Fact]
    public async Task Upload_RejectsInvalidEmptyOversizedAndExcessiveDimensions()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        foreach (var bytes in new[] { Array.Empty<byte>(), "not an image"u8.ToArray(), ImageBytes()[..32], new byte[1024 * 1024 + 1], ImageBytes(width: 6001) })
            Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(admin, product.Id, bytes)).StatusCode);
        var dto = (await admin.GetFromJsonAsync<ProductResponseDto>($"/api/products/{product.Id}"))!;
        Assert.Empty(dto.Images);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp")]
    public async Task Upload_DetectsSupportedFormats(SKEncodedImageFormat format, string expected)
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        var response = await UploadAsync(admin, product.Id, ImageBytes(format));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expected, (await response.Content.ReadFromJsonAsync<ProductImageResponseDto>())!.ContentType);
    }

    [Fact]
    public async Task ImageOperations_EnforceProductMembershipAndCount()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        var other = await CreateAsync(admin);
        var first = await AddImageAsync(admin, product.Id);
        await AddImageAsync(admin, product.Id);
        await AddImageAsync(admin, product.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(admin, product.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/products/{other.Id}/images/{first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/products/{product.Id}/images/order",
            new { imageIds = new[] { first.Id, first.Id, first.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await UploadAsync(admin, int.MaxValue)).StatusCode);
    }

    [Fact]
    public async Task LocalStorage_RejectsTraversal_AndCleanupRemovesExpiredPendingFiles()
    {
        using var admin = await AdminAsync(); // Start the isolated host.
        using var scope = factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IImageStorage>();
        foreach (var key in new[] { "../escape.png", "images/../../escape.png", "C:/escape.png", "images\\escape.png" })
            await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenReadAsync(key));
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var asset = new ImageAsset
        {
            StorageKey = $"images/{Guid.NewGuid():N}.png", State = ImageAssetState.Pending,
            DeleteAfterUtc = DateTime.UtcNow.AddMinutes(-1), UploadedBy = "test"
        };
        db.ImageAssets.Add(asset);
        await db.SaveChangesAsync();
        await storage.SaveAsync(asset.StorageKey, new MemoryStream(ImageBytes()));
        await scope.ServiceProvider.GetRequiredService<ImageCleanupService>().RunAsync(default);
        Assert.Null(await storage.OpenReadAsync(asset.StorageKey));
        Assert.False(await db.ImageAssets.AnyAsync(i => i.Id == asset.Id));
    }

    [Fact]
    public async Task ConcurrentProductChanges_AreRejected()
    {
        using var admin = await AdminAsync();
        var product = await CreateAsync(admin);
        using var first = factory.Services.CreateScope();
        using var second = factory.Services.CreateScope();
        var firstDb = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await firstDb.Products.SingleAsync(p => p.Id == product.Id);
        await secondDb.Products.SingleAsync(p => p.Id == product.Id);
        var userId = await firstDb.Users.Where(u => u.Email == "admin@company.com").Select(u => u.Id).SingleAsync();
        await first.ServiceProvider.GetRequiredService<IProductService>().UpdateAsync(product.Id, Product(), userId, default);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            second.ServiceProvider.GetRequiredService<IProductService>().UpdateAsync(product.Id, Product(), userId, default));
    }

    private async Task CleanupAsync()
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ImageCleanupService>().RunAsync(default);
    }

    [Fact]
    public async Task FailedUpload_LeavesTrackedPendingAsset_AndCleanupRetriesStorageFailures()
    {
        using var admin = await AdminAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var storage = new FailingStorage(scope.ServiceProvider.GetRequiredService<IImageStorage>());
        var service = new ImageService(db, storage, scope.ServiceProvider.GetRequiredService<IOptions<ImageStorageOptions>>());
        var userId = await db.Users.Where(u => u.Email == "admin@company.com").Select(u => u.Id).SingleAsync();
        using var content = new MemoryStream(ImageBytes());
        await Assert.ThrowsAsync<IOException>(() => service.StageAsync(content, "failed.png", userId, default));
        var asset = await db.ImageAssets.SingleAsync(i => i.OriginalFileName == "failed.png");
        Assert.Equal(ImageAssetState.Pending, asset.State);
        Assert.True(asset.DeleteAfterUtc > DateTime.UtcNow);
        asset.DeleteAfterUtc = DateTime.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        var cleanup = new ImageCleanupService(db, storage, NullLogger<ImageCleanupService>.Instance);
        await cleanup.RunAsync(default);
        Assert.True(await db.ImageAssets.AnyAsync(i => i.Id == asset.Id));
        storage.FailDeletion = false;
        await cleanup.RunAsync(default);
        Assert.False(await db.ImageAssets.AnyAsync(i => i.Id == asset.Id));
        Assert.Null(await storage.OpenReadAsync(asset.StorageKey));
    }

    private class FailingStorage(IImageStorage inner) : IImageStorage
    {
        public bool FailDeletion { get; set; } = true;
        public async Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            await inner.SaveAsync(key, content, cancellationToken);
            throw new IOException("Simulated interruption after writing the file.");
        }
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            inner.OpenReadAsync(key, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            FailDeletion ? throw new IOException("Simulated locked file.") : inner.DeleteAsync(key, cancellationToken);
    }
}
