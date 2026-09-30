using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Storage;

namespace ResourceBooking.Infrastructure.Services;

public class ProductService(ApplicationDbContext db, IImageService images, IOptions<ImageStorageOptions> options)
    : IProductService
{
    private IQueryable<Product> WithImages => db.Products.Include(p => p.Images).ThenInclude(i => i.ImageAsset);

    public async Task<ProductPageDto> ListAsync(ProductQueryDto query, bool includeDrafts, CancellationToken ct)
    {
        var products = WithImages.AsNoTracking().Where(p => includeDrafts || p.IsPublished);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            products = products.Where(p => p.Title.Contains(term) || p.Brand.Contains(term));
        }
        var count = await products.CountAsync(ct);
        var page = await products.OrderByDescending(p => p.Id).Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize).ToListAsync(ct);
        return new(page.Select(ToDto).ToList(), count, query.Page, query.PageSize);
    }

    public async Task<ProductResponseDto?> GetAsync(int id, bool includeDrafts, CancellationToken ct)
    {
        var product = await WithImages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id &&
            (includeDrafts || p.IsPublished), ct);
        return product is null ? null : ToDto(product);
    }

    public async Task<ProductResponseDto> CreateAsync(ProductWriteDto dto, CancellationToken ct)
    {
        var product = new Product();
        Apply(product, dto);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return ToDto(product);
    }

    public async Task<ProductResponseDto> UpdateAsync(int id, ProductWriteDto dto, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        Apply(product, dto);
        await SaveAsync(product, ct);
        return ToDto(product);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        product.IsPublished = false;
        // Set the flag directly so EF does not cascade-delete image links before retention expires.
        product.IsDeleted = true;
        foreach (var link in product.Images)
        {
            link.ImageAsset.State = ImageAssetState.PendingDeletion;
            link.ImageAsset.DeleteAfterUtc = DateTime.UtcNow.AddDays(options.Value.DeletedProductRetentionDays);
        }
        await SaveAsync(product, ct);
    }

    public async Task<ProductImageResponseDto> AddImageAsync(int id, Stream content, string fileName,
        string altText, string userId, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        if (product.Images.Count >= options.Value.MaxImagesPerProduct)
            throw new InvalidOperationException($"A product may have at most {options.Value.MaxImagesPerProduct} images.");
        var asset = await images.StageAsync(content, fileName, userId, ct);
        var link = new ProductImage
        {
            Product = product, ImageAsset = asset, AltText = altText.Trim(),
            SortOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1
        };
        product.Images.Add(link);
        asset.State = ImageAssetState.Ready;
        asset.DeleteAfterUtc = null;
        // SQL atomically attaches the image and marks it ready. On failure the persisted
        // pending asset remains scheduled for cleanup, including concurrency failures.
        await SaveAsync(product, ct);
        return ToImageDto(link);
    }

    public async Task ReorderImagesAsync(int id, int[] imageIds, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        if (imageIds.Length != product.Images.Count || imageIds.Distinct().Count() != imageIds.Length ||
            !imageIds.ToHashSet().SetEquals(product.Images.Select(i => i.ImageAssetId)))
            throw new InvalidOperationException("Supply every image ID belonging to this product exactly once.");
        for (var index = 0; index < imageIds.Length; index++)
            product.Images.Single(i => i.ImageAssetId == imageIds[index]).SortOrder = index;
        await SaveAsync(product, ct);
    }

    public async Task UpdateImageAsync(int id, int imageId, string altText, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        FindImage(product, imageId).AltText = altText.Trim();
        await SaveAsync(product, ct);
    }

    public async Task RemoveImageAsync(int id, int imageId, CancellationToken ct)
    {
        var product = await FindAsync(id, ct);
        var link = FindImage(product, imageId);
        db.ProductImages.Remove(link);
        product.Images.Remove(link);
        link.ImageAsset.State = ImageAssetState.PendingDeletion;
        link.ImageAsset.DeleteAfterUtc = DateTime.UtcNow;
        var index = 0;
        foreach (var remaining in product.Images.OrderBy(i => i.SortOrder)) remaining.SortOrder = index++;
        await SaveAsync(product, ct);
    }

    private async Task<Product> FindAsync(int id, CancellationToken ct) =>
        await WithImages.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new KeyNotFoundException("Product not found.");

    private static ProductImage FindImage(Product product, int imageId) =>
        product.Images.SingleOrDefault(i => i.ImageAssetId == imageId) ?? throw new KeyNotFoundException("Product image not found.");

    private async Task SaveAsync(Product product, CancellationToken ct)
    {
        product.Version = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
    }

    private static void Apply(Product product, ProductWriteDto dto)
    {
        product.Title = dto.Title.Trim();
        product.Description = dto.Description?.Trim() ?? string.Empty;
        product.Brand = dto.Brand.Trim();
        product.Price = dto.Price;
        product.Currency = dto.Currency.ToUpperInvariant();
        product.IsPublished = dto.IsPublished;
    }

    private static ProductImageResponseDto ToImageDto(ProductImage link) => new(link.ImageAssetId,
        link.SortOrder, link.AltText, link.ImageAsset.Width, link.ImageAsset.Height,
        link.ImageAsset.ContentType, link.ImageAsset.Size,
        $"/api/images/{link.ImageAssetId}/content", $"/api/images/{link.ImageAssetId}/download");

    private static ProductResponseDto ToDto(Product product) => new(product.Id, product.Title,
        product.Description, product.Brand, product.Price, product.Currency, product.IsPublished,
        product.CreatedAt, product.Images.Where(i => i.ImageAsset.State == ImageAssetState.Ready)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ImageAssetId).Select(ToImageDto).ToList());
}
