using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Storage;
using SkiaSharp;

namespace ResourceBooking.Infrastructure.Services;

public class ImageService(ApplicationDbContext db, IImageStorage storage, IOptions<ImageStorageOptions> options)
    : IImageService
{
    public async Task<ImageAsset> StageAsync(Stream content, string fileName, string userId, CancellationToken ct)
    {
        var limits = options.Value;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > limits.MaxFileSizeBytes)
                throw new InvalidOperationException($"Image exceeds the {limits.MaxFileSizeBytes} byte limit.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
        }
        if (buffer.Length == 0) throw new InvalidOperationException("Image must not be empty.");

        using var data = SKData.CreateCopy(buffer.ToArray());
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new InvalidOperationException("The file is not a supported image.");
        var (contentType, extension) = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => ("image/jpeg", ".jpg"),
            SKEncodedImageFormat.Png => ("image/png", ".png"),
            SKEncodedImageFormat.Webp => ("image/webp", ".webp"),
            _ => throw new InvalidOperationException("Only JPEG, PNG and WebP images are supported.")
        };
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > limits.MaxDimension ||
            info.Height > limits.MaxDimension || (long)info.Width * info.Height > limits.MaxPixels)
            throw new InvalidOperationException("Image dimensions exceed the configured limits.");
        if (codec.FrameCount > 1) throw new InvalidOperationException("Animated images are not supported.");
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new InvalidOperationException("Image content is corrupt or incomplete.");

        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length == 0) name = "image" + extension;
        if (name.Length > 255) name = name[..255];
        var asset = new ImageAsset
        {
            StorageKey = $"images/{Guid.NewGuid():N}{extension}", OriginalFileName = name,
            ContentType = contentType, Size = buffer.Length, Width = info.Width, Height = info.Height,
            UploadedBy = userId, State = ImageAssetState.Pending, DeleteAfterUtc = DateTime.UtcNow.AddDays(1)
        };
        // Persist the cleanup record before writing any file. A crash cannot leave an untracked file.
        db.ImageAssets.Add(asset);
        await db.SaveChangesAsync(ct);
        buffer.Position = 0;
        await storage.SaveAsync(asset.StorageKey, buffer, ct);
        return asset;
    }

    public async Task<ImageContent?> OpenAsync(int id, bool isAdmin, CancellationToken ct)
    {
         //Access is based on a visible attachment, not on possession of an image ID.
        // Add separate note/profile policies here when those attachment types are introduced.
        var asset = await db.ProductImages.AsNoTracking()
            .Where(i => i.ImageAssetId == id && (isAdmin || i.Product.IsPublished) &&
                i.ImageAsset.State == ImageAssetState.Ready)
            .Select(i => i.ImageAsset).FirstOrDefaultAsync(ct);
        if (asset is null) return null;
        var stream = await storage.OpenReadAsync(asset.StorageKey, ct);
        return stream is null ? null : new ImageContent(stream, asset.ContentType, asset.OriginalFileName);
    }
}
