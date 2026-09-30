using ResourceBooking.Core.Enums;

using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Core.Entities;

public class Booking : BaseEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }

    public BookingStatus Status { get; set; } = BookingStatus.Pending;
    public string Purpose { get; set; } = string.Empty;
    public string? AdminRejectionReason { get; set; }

    // Concurrency Token for optimistic concurrency control during simultaneous booking attempts
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
