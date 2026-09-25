using ResourceBooking.Core.Entities;

namespace ResourceBooking.Core.Interfaces;

public interface IBookingRepository : IGenericRepository<Booking>
{
    Task<bool> HasOverlappingBookingAsync(int resourceId, DateTime start, DateTime end, int? excludeBookingId = null);
    Task<IReadOnlyList<Booking>> GetUserBookingsAsync(string userId);
    Task<IReadOnlyList<Booking>> GetPendingBookingsAsync();
    Task<Booking?> GetBookingWithDetailsAsync(int id);
}