using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.Entities;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.IntegrationTests;

public class EntityLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SaveOverloads_SoftDeleteOptedInEntities_AndHardDeleteImages(int overload)
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var todo = new TodoItem { Title = "Keep this row", UserId = "test" };
        var image = new ImageAsset { StorageKey = "images/test.png", UploadedBy = "test" };
        db.AddRange(todo, image);
        await SaveAsync(db, overload);
        var createdAt = image.CreatedAt;
        Assert.Null(image.UpdatedAt);

        image.State = ImageAssetState.Ready;
        await SaveAsync(db, overload);
        Assert.Equal(createdAt, image.CreatedAt);
        Assert.NotNull(image.UpdatedAt);

        db.RemoveRange(todo, image);
        await SaveAsync(db, overload);
        db.ChangeTracker.Clear();

        Assert.Empty(await db.TodoItems.ToListAsync());
        var retained = await db.TodoItems.IgnoreQueryFilters().SingleAsync();
        Assert.True(retained.IsDeleted);
        Assert.NotNull(retained.UpdatedAt);
        Assert.Empty(await db.ImageAssets.IgnoreQueryFilters().ToListAsync());
    }

    private static Task<int> SaveAsync(ApplicationDbContext db, int overload) => overload switch
    {
        0 => Task.FromResult(db.SaveChanges()),
        1 => Task.FromResult(db.SaveChanges(true)),
        2 => db.SaveChangesAsync(),
        _ => db.SaveChangesAsync(true)
    };
}
