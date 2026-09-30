namespace ResourceBooking.Core.Interfaces;

public interface IImageStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
