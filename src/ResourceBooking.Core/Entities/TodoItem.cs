namespace ResourceBooking.Core.Entities;

public class TodoItem : BaseEntity
{
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
