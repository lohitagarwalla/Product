namespace ResourceBooking.Infrastructure.Storage;

public class ImageStorageOptions
{
    public string RootPath { get; set; } = "App_Data/images";
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxDimension { get; set; } = 6000;
    public long MaxPixels { get; set; } = 16000000;
    public int MaxImagesPerProduct { get; set; } = 10;
    public int DeletedProductRetentionDays { get; set; } = 30;
    public bool CleanupEnabled { get; set; } = true;
}
