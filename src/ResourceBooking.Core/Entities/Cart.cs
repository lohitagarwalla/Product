namespace ResourceBooking.Core.Entities;

public class Cart : BaseEntity
{
    public string UserId { get; set; } = string.Empty;
    public int? SelectedAddressId { get; set; }
    public UserAddress? SelectedAddress { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

public class CartItem : BaseEntity
{
    public int CartId { get; set; }
    public Cart Cart { get; set; } = null!;
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
