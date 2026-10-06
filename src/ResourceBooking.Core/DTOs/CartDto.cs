using System.ComponentModel.DataAnnotations;

namespace ResourceBooking.Core.DTOs;

public class CartItemWriteDto
{
    [Range(1, 1000)] public int Quantity { get; set; }
}

public class CartAddressWriteDto
{
    [Range(1, int.MaxValue)] public int? AddressId { get; set; }
}

public record CartItemResponseDto(int ProductId, int Quantity, bool Available,
    string? ProductTitle, decimal? UnitPrice, string? Currency, string? ImageUrl);
public record CartResponseDto(int Id, int? SelectedAddressId,
    AddressResponseDto? SelectedAddress, IReadOnlyList<CartItemResponseDto> Items);
public record OrderDeliveryAddressDto(string RecipientName, string AddressLine1,
    string? AddressLine2, string City, string State, string PostalCode, string CountryCode);
