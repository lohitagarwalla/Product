using System.ComponentModel.DataAnnotations;

namespace ResourceBooking.Core.DTOs;

public class TodoCreateDto
{
    [Required, StringLength(250)]
    public string Title { get; set; } = string.Empty;
}

public class TodoResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
}
