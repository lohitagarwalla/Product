namespace ResourceBooking.Infrastructure.Services;

internal static class AddressPhoneNumber
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var phone = value.Trim();
        return phone.StartsWith("+91", StringComparison.Ordinal) ? phone : $"+91{phone}";
    }
}
