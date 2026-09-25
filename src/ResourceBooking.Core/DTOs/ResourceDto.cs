using System.ComponentModel.DataAnnotations;
using ResourceBooking.Core.Enums;

namespace ResourceBooking.Core.DTOs;

public class ResourceCreateDto
{
    [Required(ErrorMessage = "Resource name is required.")]
    [StringLength(100, MinimumLength = 3, ErrorMessage = "Name must be between 3 and 100 characters.")]
    public string Name { get; set; } = string.Empty;

    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters.")]
    public string Description { get; set; } = string.Empty;

    [Required(ErrorMessage = "Resource type is required.")]
    public ResourceType Type { get; set; }

    [Range(1, 500, ErrorMessage = "Capacity must be between 1 and 500.")]
    public int Capacity { get; set; }

    [Required(ErrorMessage = "Location is required.")]
    [StringLength(150, ErrorMessage = "Location cannot exceed 150 characters.")]
    public string Location { get; set; } = string.Empty;
}

public class ResourceResponseDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ResourceType Type { get; set; }
    public int Capacity { get; set; }
    public string Location { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}