using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Core.Entities;

public class TodoItem : BaseEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }
    public string Title { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public string UserId { get; set; } = string.Empty;
    public ApplicationUser User { get; set; } = null!;
}
