namespace ResourceBooking.Core.Entities;

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public RefreshSession Session { get; set; } = null!;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    // Retain consumed hashes until the session expires so replay can be detected.
    public DateTime? UsedAt { get; set; }
}
