using System.ComponentModel.DataAnnotations;
using ResourceBooking.Core.Enums;
using ResourceBooking.Core.Validation;

namespace ResourceBooking.Core.DTOs;

public class BookingCreateDto
{
    [Required(ErrorMessage = "Selecting a resource is required.")]
    public int ResourceId { get; set; }

    [Required(ErrorMessage = "Start time is required.")]
    [DataType(DataType.DateTime)]
    public DateTime StartTime { get; set; } = DateTime.UtcNow.AddHours(1);

    [Required(ErrorMessage = "End time is required.")]
    [DataType(DataType.DateTime)]
    [DateGreaterThan("StartTime", ErrorMessage = "End time must be after the start time.")]
    public DateTime EndTime { get; set; } = DateTime.UtcNow.AddHours(2);

    [Required(ErrorMessage = "Please state the purpose of this reservation.")]
    [StringLength(250, MinimumLength = 5, ErrorMessage = "Purpose must be between 5 and 250 characters.")]
    public string Purpose { get; set; } = string.Empty;
}

public class BookingResponseDto
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public string ResourceName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public BookingStatus Status { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string? AdminRejectionReason { get; set; }
}