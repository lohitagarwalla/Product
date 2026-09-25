using Microsoft.AspNetCore.Identity;

namespace ResourceBooking.Core.Entities;

public class ApplicationUser : IdentityUser
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Property: One user can have many bookings
    public ICollection<Booking> Bookings { get; set; } = new List<Booking>();
}