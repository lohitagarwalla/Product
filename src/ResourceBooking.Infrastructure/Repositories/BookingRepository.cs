using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Repositories;

public class BookingRepository : GenericRepository<Booking>, IBookingRepository
{
    public BookingRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<bool> HasOverlappingBookingAsync(int resourceId, DateTime start, DateTime end, int? excludeBookingId = null)
    {
        // CORE ALGORITHM: Two intervals [A, B] and [C, D] overlap if and only if A < D AND B > C
        return await _context.Bookings
            .AnyAsync(b => b.ResourceId == resourceId
                        && (excludeBookingId == null || b.Id != excludeBookingId)
                        && b.Status != BookingStatus.Cancelled
                        && b.Status != BookingStatus.Rejected
                        && b.StartTime < end
                        && b.EndTime > start);
    }

    public async Task<IReadOnlyList<Booking>> GetUserBookingsAsync(string userId)
    {
        return await _context.Bookings
            .Include(b => b.Resource)
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.StartTime)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Booking>> GetPendingBookingsAsync()
    {
        return await _context.Bookings
            .Include(b => b.Resource)
            .Include(b => b.User)
            .Where(b => b.Status == BookingStatus.Pending)
            .OrderBy(b => b.CreatedAt)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<Booking?> GetBookingWithDetailsAsync(int id)
    {
        return await _context.Bookings
            .Include(b => b.Resource)
            .Include(b => b.User)
            .FirstOrDefaultAsync(b => b.Id == id);
    }
}
