using System.ComponentModel.DataAnnotations;

namespace ResourceBooking.Core.DTOs;

public class ProductPriceHistoryQueryDto
{
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
}

public record ProductPriceHistoryResponseDto(int Id, int ProductId, decimal? PreviousPrice,
    decimal NewPrice, string? ChangedByUserId, DateTime ChangedAtUtc, string EntryType, string? ChangedByUserName = null);

public record ProductPriceHistoryPageDto(IReadOnlyList<ProductPriceHistoryResponseDto> Items,
    int TotalCount, int Page, int PageSize);
