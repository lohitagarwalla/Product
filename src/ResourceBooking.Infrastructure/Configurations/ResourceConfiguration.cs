using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(r => r.Description)
            .HasMaxLength(500);

        builder.Property(r => r.Location)
            .IsRequired()
            .HasMaxLength(150);

        builder.Property(r => r.Type)
            .IsRequired()
            .HasConversion<int>(); // Stores Enum as integer in SQL Server

        // Global Query Filter: Automatically hides soft-deleted resources across all LINQ queries
        builder.HasQueryFilter(r => !r.IsDeleted);
    }
}