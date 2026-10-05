namespace ResourceBooking.Core.Entities;

public class UserAddress : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
    public string? Label { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string AddressLine1 { get; set; } = string.Empty;
    public string? AddressLine2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string CountryCode { get; set; } = "IN";
    public bool IsDefault { get; set; }
    public byte[] RowVersion { get; set; } = new byte[8];
}
