using ResourceBooking.Core.Interfaces;

namespace ResourceBooking.Core.Entities;

public class Product : BaseEntity, ISoftDeletable
{
    public bool IsDeleted { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "INR";
    public bool IsPublished { get; set; }
    // Every edit, including image changes, participates in optimistic concurrency.
    public Guid Version { get; set; } = Guid.NewGuid();
    public ICollection<ProductImage> Images { get; set; } = new List<ProductImage>();
}
