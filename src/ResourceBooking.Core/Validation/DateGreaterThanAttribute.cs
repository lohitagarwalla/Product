using System.ComponentModel.DataAnnotations;

namespace ResourceBooking.Core.Validation;

[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class DateGreaterThanAttribute : ValidationAttribute
{
    private readonly string _comparisonProperty;

    public DateGreaterThanAttribute(string comparisonProperty)
    {
        _comparisonProperty = comparisonProperty;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var currentValue = value as DateTime?;
        var property = validationContext.ObjectType.GetProperty(_comparisonProperty);

        if (property == null)
        {
            return new ValidationResult($"Property '{_comparisonProperty}' not found.");
        }

        var comparisonValue = property.GetValue(validationContext.ObjectInstance) as DateTime?;

        if (currentValue.HasValue && comparisonValue.HasValue && currentValue <= comparisonValue)
        {
            return new ValidationResult(ErrorMessage ?? $"{validationContext.DisplayName} must be later than {_comparisonProperty}.");
        }

        return ValidationResult.Success;
    }
}
