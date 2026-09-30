namespace ResourceBooking.Core.Entities;

public enum ImageAssetState { Pending, Ready, PendingDeletion }

public class ImageAsset : BaseEntity
{
    public string StorageKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long Size { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public ImageAssetState State { get; set; }
    // Durable cleanup schedule also covers uploads interrupted before attachment.
    public DateTime? DeleteAfterUtc { get; set; }
}
