namespace ResourceBooking.Core.Entities;

// One independent login session (for example, one browser/device).
public class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = string.Empty;
    public string SecurityStamp { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
