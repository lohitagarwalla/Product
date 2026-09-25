using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Infrastructure.Services;

public class BookingService : IBookingService
{
    private readonly IBookingRepository _bookingRepository;
    private readonly IGenericRepository<Resource> _resourceRepository;

    public BookingService(
        IBookingRepository bookingRepository,
        IGenericRepository<Resource> resourceRepository)
    {
        _bookingRepository = bookingRepository;
        _resourceRepository = resourceRepository;
    }

    public async Task<BookingResponseDto> CreateBookingAsync(BookingCreateDto dto, string userId)
    {
        // 1. Business Rule: Ensure resource exists and is active
        var resource = await _resourceRepository.GetByIdAsync(dto.ResourceId);
        if (resource == null || !resource.IsActive)
        {
            throw new InvalidOperationException("The requested resource is unavailable or does not exist.");
        }

        // 2. Business Rule: Check for time-slot overlap
        bool isOverlapping = await _bookingRepository.HasOverlappingBookingAsync(dto.ResourceId, dto.StartTime, dto.EndTime);
        if (isOverlapping)
        {
            throw new InvalidOperationException("The selected resource is already booked for the specified time slot.");
        }

        // 3. Map DTO to Entity
        var booking = new Booking
        {
            ResourceId = dto.ResourceId,
            UserId = userId,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Purpose = dto.Purpose,
            Status = BookingStatus.Pending
        };

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync();

        // 4. Return Response DTO
        return new BookingResponseDto
        {
            Id = booking.Id,
            ResourceId = resource.Id,
            ResourceName = resource.Name,
            UserId = userId,
            StartTime = booking.StartTime,
            EndTime = booking.EndTime,
            Status = booking.Status,
            Purpose = booking.Purpose
        };
    }

    public async Task<bool> CancelBookingAsync(int bookingId, string userId, bool isAdmin = false)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId);
        if (booking == null) return false;

        // User can only cancel their own booking unless they are an Admin
        if (!isAdmin && booking.UserId != userId)
        {
            throw new UnauthorizedAccessException("You can only cancel your own reservations.");
        }

        if (booking.StartTime <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Cannot cancel a reservation that has already started or completed.");
        }

        booking.Status = BookingStatus.Cancelled;
        _bookingRepository.Update(booking);
        return await _bookingRepository.SaveChangesAsync();
    }

    public async Task<bool> ApproveBookingAsync(int bookingId)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId);
        if (booking == null) return false;

        booking.Status = BookingStatus.Confirmed;
        _bookingRepository.Update(booking);
        return await _bookingRepository.SaveChangesAsync();
    }

    public async Task<bool> RejectBookingAsync(int bookingId, string rejectionReason)
    {
        var booking = await _bookingRepository.GetByIdAsync(bookingId);
        if (booking == null) return false;

        booking.Status = BookingStatus.Rejected;
        booking.AdminRejectionReason = rejectionReason;
        _bookingRepository.Update(booking);
        return await _bookingRepository.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetUserBookingsAsync(string userId)
    {
        var bookings = await _bookingRepository.GetUserBookingsAsync(userId);
        return bookings.Select(b => new BookingResponseDto
        {
            Id = b.Id,
            ResourceId = b.ResourceId,
            ResourceName = b.Resource?.Name ?? "Unknown",
            UserId = b.UserId,
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Status = b.Status,
            Purpose = b.Purpose,
            AdminRejectionReason = b.AdminRejectionReason
        }).ToList();
    }

    public async Task<IReadOnlyList<BookingResponseDto>> GetPendingBookingsAsync()
    {
        var bookings = await _bookingRepository.GetPendingBookingsAsync();
        return bookings.Select(b => new BookingResponseDto
        {
            Id = b.Id,
            ResourceId = b.ResourceId,
            ResourceName = b.Resource?.Name ?? "Unknown",
            UserId = b.UserId,
            UserName = $"{b.User?.FirstName} {b.User?.LastName}".Trim(),
            StartTime = b.StartTime,
            EndTime = b.EndTime,
            Status = b.Status,
            Purpose = b.Purpose
        }).ToList();
    }
}
