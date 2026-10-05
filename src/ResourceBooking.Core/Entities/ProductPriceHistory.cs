namespace ResourceBooking.Core.Entities;

public enum ProductPriceHistoryEntryType { Created, PriceChanged, Baseline }

// Audit entries have no edit or soft-delete lifecycle. Baselines have an unknown actor.
public class ProductPriceHistory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public decimal? PreviousPrice { get; set; }
    public decimal NewPrice { get; set; }
    public string? ChangedByUserId { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public ProductPriceHistoryEntryType EntryType { get; set; }
}
