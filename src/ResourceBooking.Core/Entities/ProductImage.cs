namespace ResourceBooking.Core.Entities;

public class ProductImage
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int ImageAssetId { get; set; }
    public ImageAsset ImageAsset { get; set; } = null!;
    public int SortOrder { get; set; }
    public string AltText { get; set; } = string.Empty;
}
