using ResourceBooking.Core.Entities;

namespace ResourceBooking.Core.Interfaces;

public interface IImageService
{
    Task<ImageAsset> StageAsync(Stream content, string fileName, string userId, CancellationToken cancellationToken);
    Task<ImageContent?> OpenAsync(int id, bool isAdmin, CancellationToken cancellationToken);
}

public record ImageContent(Stream Stream, string ContentType, string FileName);
