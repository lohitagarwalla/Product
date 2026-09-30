using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Infrastructure.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ImageAsset> ImageAssets => Set<ImageAsset>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Calling base.OnModelCreating is MANDATORY when extending IdentityDbContext
        base.OnModelCreating(builder);

        // Automatically scans assembly and registers all IEntityTypeConfiguration implementations
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void PrepareChanges()
    {
        // Soft deletion is opt-in, independent of shared identity and timestamps.
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>()
                     .Where(e => e.State == EntityState.Deleted).ToList())
        {
            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
        }

        var now = DateTime.UtcNow;
        var entries = ChangeTracker.Entries<BaseEntity>();

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }

    }
}
