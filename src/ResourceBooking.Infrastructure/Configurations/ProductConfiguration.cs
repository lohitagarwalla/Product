using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table => table.HasCheckConstraint("CK_Products_Price", "[Price] >= 0"));
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Title).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000).IsRequired();
        builder.Property(p => p.Brand).HasMaxLength(100).IsRequired();
        builder.Property(p => p.Price).HasPrecision(18, 2);
        builder.Property(p => p.Currency).HasMaxLength(3).IsRequired();
        builder.Property(p => p.Version).IsConcurrencyToken();
        // HasIndex defines a database index: a lookup structure that can help the database
        // find matching rows without scanning the whole Products table.
        // This is one composite index containing IsDeleted, IsPublished, and Id, in that order.
        // Column order matters: it can help queries filtering by IsDeleted and IsPublished,
        // then ordering by Id. The database decides whether to use it for each query.
        // It is not a uniqueness constraint unless IsUnique() is added.
        // Indexes use extra storage and add work when indexed data changes.
        // Create and apply an EF Core migration to add this index to the database.
        builder.HasIndex(p => new { p.IsDeleted, p.IsPublished, p.Id });
        builder.HasQueryFilter(p => !p.IsDeleted);
    }
}

public class ImageAssetConfiguration : IEntityTypeConfiguration<ImageAsset>
{
    public void Configure(EntityTypeBuilder<ImageAsset> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.StorageKey).HasMaxLength(300).IsRequired();
        builder.HasIndex(i => i.StorageKey).IsUnique();
        builder.Property(i => i.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(i => i.ContentType).HasMaxLength(50).IsRequired();
        builder.Property(i => i.UploadedBy).HasMaxLength(450).IsRequired();
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(i => i.UploadedBy).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => new { i.State, i.DeleteAfterUtc });
    }
}

public class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.HasKey(i => new { i.ProductId, i.ImageAssetId }); // Composite primary key: an image asset can be linked to the same product only once.
        builder.Property(i => i.AltText).HasMaxLength(250).IsRequired(); // Required (non-null) alternative text, limited to 250 characters.
        builder.HasOne(i => i.Product).WithMany(p => p.Images).HasForeignKey(i => i.ProductId); // One product has many image links; ProductId is the foreign key.
        builder.HasOne(i => i.ImageAsset).WithMany().HasForeignKey(i => i.ImageAssetId); // One asset can have many links; WithMany() configures no reverse navigation collection.
        builder.HasIndex(i => new { i.ProductId, i.SortOrder }); // Speeds up queries for a product's images ordered by SortOrder; does not sort results itself.
        builder.HasQueryFilter(i => !i.Product.IsDeleted); // Hides links to soft-deleted products in normal EF queries; rows remain stored.
    }
}
