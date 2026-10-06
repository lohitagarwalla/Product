using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace ResourceBooking.Core.DTOs;

public abstract class AddressWriteDto : IValidatableObject
{
    [StringLength(50)] public string? Label { get; set; }
    [Required, StringLength(100)] public string RecipientName { get; set; } = string.Empty;
    [StringLength(32)] public string? PhoneNumber { get; set; }
    [Required, StringLength(200)] public string AddressLine1 { get; set; } = string.Empty;
    [StringLength(200)] public string? AddressLine2 { get; set; }
    [Required, StringLength(100)] public string City { get; set; } = string.Empty;
    [Required, StringLength(100)] public string State { get; set; } = string.Empty;
    [Required, StringLength(20)] public string PostalCode { get; set; } = string.Empty;
    [Required, RegularExpression(@"\A[A-Za-z]{2}\z", ErrorMessage = "Use a two-letter country code, such as IN.")]
    public string CountryCode { get; set; } = "IN";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(PhoneNumber) &&
            !Regex.IsMatch(PhoneNumber.Trim(), @"\A(?:\+91)?[6-9][0-9]{9}\z"))
            yield return new ValidationResult("Enter a valid Indian mobile number: 10 digits starting with 6, 7, 8, or 9, optionally prefixed with +91.", [nameof(PhoneNumber)]);
        if (string.Equals(CountryCode, "IN", StringComparison.OrdinalIgnoreCase) &&
            !Regex.IsMatch(PostalCode?.Trim() ?? "", @"\A[1-9][0-9]{5}\z"))
            yield return new ValidationResult("Enter a six-digit Indian PIN code that does not start with zero.", [nameof(PostalCode)]);
    }
}

public class AddressCreateDto : AddressWriteDto
{
    public bool MakeDefault { get; set; }
}

public class AddressUpdateDto : AddressWriteDto
{
    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; set; } = [];
}

public class AddressVersionDto
{
    // JSON represents byte[] as a Base64 string. Send the value returned by GET.
    [Required, MinLength(8), MaxLength(8)]
    public byte[] RowVersion { get; set; } = [];
}

public record AddressResponseDto(int Id, string? Label, string RecipientName, string AddressLine1,
    string? AddressLine2, string City, string State, string PostalCode, string CountryCode,
    bool IsDefault, byte[] RowVersion, DateTime CreatedAt, DateTime? UpdatedAt, string? PhoneNumber = null);
