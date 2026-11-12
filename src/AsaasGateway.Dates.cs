using System.Globalization;
using System.Text.RegularExpressions;

namespace Sufficit.Gateway.Asaas;

public sealed partial class AsaasGateway
{
    // Maintainer contract (2026-11-12): honor explicit Z/offsets; otherwise use
    // fixed GMT-3 (Brasília), including historical values. Never use server time
    // or São Paulo's historical DST rules for an offset-less provider value.
    private static DateTimeOffset? ParseProviderDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.Length > 10 && Regex.IsMatch(value, @"(?:[zZ]|[+-]\d{2}(?::?\d{2})?)$"))
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AdjustToUniversal, out var explicitTime)
                    ? explicitTime : null;

        // A receipt date becomes midnight GMT-3. Civil due dates use DateOnly
        // elsewhere and must not pass through this instant conversion.
        string[] formats = { "yyyy-MM-dd", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF" };
        if (!DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out var localTime)) return null;
        return new DateTimeOffset(DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            TimeSpan.FromHours(-3)).ToUniversalTime();
    }
}
