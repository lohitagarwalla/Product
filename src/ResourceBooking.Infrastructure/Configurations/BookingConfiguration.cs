using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ResourceBooking.Core.Entities;

namespace ResourceBooking.Infrastructure.Configurations;

public class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("Bookings");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Purpose)
            .IsRequired()
            .HasMaxLength(250);

        builder.Property(b => b.Status)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(b => b.AdminRejectionReason)
            .HasMaxLength(500);

        // Optimistic Concurrency Control
        builder.Property(b => b.RowVersion)
            .IsRowVersion();

        // Relationship: Resource -> Bookings (Prevent Cascade Delete)
        builder.HasOne(b => b.Resource)
            .WithMany(r => r.Bookings)
            .HasForeignKey(b => b.ResourceId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship: ApplicationUser -> Bookings (Prevent Cascade Delete)
        builder.HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Global Query Filter for Soft Delete
        builder.HasQueryFilter(b => !b.IsDeleted);
    }
}