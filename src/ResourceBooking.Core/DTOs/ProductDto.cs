using System.ComponentModel.DataAnnotations;

namespace ResourceBooking.Core.DTOs;

public class ProductWriteDto : IValidatableObject
{
    [Required, StringLength(200)]
    public string Title { get; set; } = string.Empty;
    
    [StringLength(4000)] 
    public string Description { get; set; } = string.Empty;
    
    [Required, StringLength(100)] 
    public string Brand { get; set; } = string.Empty;
    
    [Range(typeof(decimal), "0", "9999999999999999.99")]
    public decimal Price { get; set; }
    
    [Required, RegularExpression("^[A-Za-z]{3}$")]
    public string Currency { get; set; } = "INR";
    
    public bool IsPublished { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (decimal.Round(Price, 2) != Price)
            yield return new ValidationResult("Price may have at most two decimal places.", [nameof(Price)]);
    }
}

public class ProductQueryDto
{
    [Range(1, 1000000)] public int Page { get; set; } = 1;
    [Range(1, 100)] public int PageSize { get; set; } = 20;
    [StringLength(200)] public string? Search { get; set; }
}

public record ProductImageResponseDto(int Id, int SortOrder, string AltText, int Width, int Height,
    string ContentType, long Size, string Url, string DownloadUrl);
public record ProductResponseDto(int Id, string Title, string Description, string Brand, decimal Price,
    string Currency, bool IsPublished, DateTime CreatedAt, IReadOnlyList<ProductImageResponseDto> Images);
public record ProductPageDto(IReadOnlyList<ProductResponseDto> Items, int TotalCount, int Page, int PageSize);

public class ProductImageOrderDto
{
    [Required, MinLength(1), MaxLength(100)]
    public int[] ImageIds { get; set; } = [];
}

public class ProductImageUpdateDto
{
    [Required(AllowEmptyStrings = true), StringLength(250)]
    public string AltText { get; set; } = string.Empty;
}
