using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Core.Interfaces;

public interface IProductService
{
    Task<ProductPageDto> ListAsync(ProductQueryDto query, bool includeDrafts, CancellationToken ct);
    Task<ProductResponseDto?> GetAsync(int id, bool includeDrafts, CancellationToken ct);
    Task<ProductResponseDto> CreateAsync(ProductWriteDto dto, string userId, CancellationToken ct);
    Task<ProductResponseDto> UpdateAsync(int id, ProductWriteDto dto, string userId, CancellationToken ct);
    Task<ProductPriceHistoryPageDto> GetPriceHistoryAsync(int id, ProductPriceHistoryQueryDto query, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<ProductImageResponseDto> AddImageAsync(int id, Stream content, string fileName, string altText, string userId, CancellationToken ct);
    Task ReorderImagesAsync(int id, int[] imageIds, CancellationToken ct);
    Task UpdateImageAsync(int id, int imageId, string altText, CancellationToken ct);
    Task RemoveImageAsync(int id, int imageId, CancellationToken ct);
}
