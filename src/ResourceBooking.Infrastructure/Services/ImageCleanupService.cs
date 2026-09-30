using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Storage;

namespace ResourceBooking.Infrastructure.Services;

public class ImageCleanupService(ApplicationDbContext db, IImageStorage storage, ILogger<ImageCleanupService> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var due = await db.ImageAssets.Where(i => i.State != ImageAssetState.Ready &&
            i.DeleteAfterUtc <= DateTime.UtcNow).OrderBy(i => i.DeleteAfterUtc).Take(100).ToListAsync(ct);
        foreach (var asset in due)
        {
            // Do not remove bytes still referenced by a live product.
            if (await db.ProductImages.IgnoreQueryFilters().AnyAsync(i => i.ImageAssetId == asset.Id && !i.Product.IsDeleted, ct))
                continue;
            try
            {
                await storage.DeleteAsync(asset.StorageKey, ct);
                var links = await db.ProductImages.IgnoreQueryFilters().Where(i => i.ImageAssetId == asset.Id).ToListAsync(ct);
                db.ProductImages.RemoveRange(links);
                db.ImageAssets.Remove(asset);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Image cleanup failed for asset {ImageId}; it will be retried.", asset.Id);
                // Avoid carrying a failed deletion into the next asset's SaveChanges.
                db.ChangeTracker.Clear();
            }
        }
    }
}

public class ImageCleanupWorker(IServiceScopeFactory scopes, IOptions<ImageStorageOptions> options,
    ILogger<ImageCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CleanupEnabled) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            do
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ImageCleanupService>().RunAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Image cleanup could not run; it will be retried.");
                }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
