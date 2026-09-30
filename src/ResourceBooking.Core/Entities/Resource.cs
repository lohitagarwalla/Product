using ResourceBooking.Core.Enums;

using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Core.Entities;

public class Resource : BaseEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ResourceType Type { get; set; }
    public int Capacity { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    // Navigation Property: One resource can have many bookings over time
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}
