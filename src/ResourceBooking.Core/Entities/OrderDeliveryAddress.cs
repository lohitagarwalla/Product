namespace ResourceBooking.Core.Entities;

// A value snapshot, deliberately independent of the editable address book.
public class OrderDeliveryAddress
{
    public string RecipientName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}
