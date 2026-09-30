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
        builder.HasKey(i => new { i.ProductId, i.ImageAssetId });
        builder.Property(i => i.AltText).HasMaxLength(250).IsRequired();
        builder.HasOne(i => i.Product).WithMany(p => p.Images).HasForeignKey(i => i.ProductId);
        builder.HasOne(i => i.ImageAsset).WithMany().HasForeignKey(i => i.ImageAssetId);
        builder.HasIndex(i => new { i.ProductId, i.SortOrder });
        builder.HasQueryFilter(i => !i.Product.IsDeleted);
    }
}
