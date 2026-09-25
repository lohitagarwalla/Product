using ResourceBooking.Core.DTOs;

namespace ResourceBooking.Core.Interfaces;

public interface IBookingService
{
    Task<BookingResponseDto> CreateBookingAsync(BookingCreateDto dto, string userId);
    Task<bool> CancelBookingAsync(int bookingId, string userId, bool isAdmin = false);
    Task<bool> ApproveBookingAsync(int bookingId);
    Task<bool> RejectBookingAsync(int bookingId, string rejectionReason);
    Task<IReadOnlyList<BookingResponseDto>> GetUserBookingsAsync(string userId);
    Task<IReadOnlyList<BookingResponseDto>> GetPendingBookingsAsync();
}