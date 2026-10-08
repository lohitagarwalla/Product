using ResourceBooking.Core.Constants;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[Authorize]
public class BookingsController : ControllerBase
{
    private readonly IBookingService _bookingService;

    public BookingsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    /// <summary>
    /// Submit a new time-slot reservation request.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(BookingResponseDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BookingResponseDto>> CreateBooking([FromBody] BookingCreateDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await _bookingService.CreateBookingAsync(dto, userId);
            return CreatedAtAction(nameof(GetMyBookings), new { id = result.Id }, result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Retrieve all reservations for the currently authenticated user.
    /// </summary>
    [HttpGet("my-bookings")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<BookingResponseDto>))]
    public async Task<ActionResult<IEnumerable<BookingResponseDto>>> GetMyBookings()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var bookings = await _bookingService.GetUserBookingsAsync(userId);
        return Ok(bookings);
    }

    /// <summary>
    /// Cancel a reservation.
    /// </summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelBooking(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        bool isAdmin = User.IsInRole(Roles.Admin);

        try
        {
            bool success = await _bookingService.CancelBookingAsync(id, userId, isAdmin);
            if (!success) return NotFound();

            return Ok(new { message = "Booking reservation cancelled successfully." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Fetch all pending bookings requiring approval (Admin Only).
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IEnumerable<BookingResponseDto>))]
    public async Task<ActionResult<IEnumerable<BookingResponseDto>>> GetPendingBookings()
    {
        var pendingBookings = await _bookingService.GetPendingBookingsAsync();
        return Ok(pendingBookings);
    }

    /// <summary>
    /// Confirm and approve a pending booking (Admin Only).
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveBooking(int id)
    {
        bool success = await _bookingService.ApproveBookingAsync(id);
        if (!success) return NotFound();

        return Ok(new { message = "Booking confirmed successfully." });
    }

    /// <summary>
    /// Reject a pending booking request with reason (Admin Only).
    /// </summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectBooking(int id, [FromBody] string rejectionReason)
    {
        if (string.IsNullOrWhiteSpace(rejectionReason))
        {
            return BadRequest(new { message = "A rejection reason must be provided." });
        }

        bool success = await _bookingService.RejectBookingAsync(id, rejectionReason);
        if (!success) return NotFound();

        return Ok(new { message = "Booking request rejected." });
    }
}
