#if WINDOWS
using System.Globalization;
using Windows.Foundation.Metadata;
using Windows.Globalization.NumberFormatting;

namespace Plugin.Maui.Currency;

internal static class WindowsCurrencySymbols
{
    public static string GetSymbol(string code, IEnumerable<string>? languages = null, string geographicRegion = "")
    {
        ArgumentNullException.ThrowIfNull(code);
        // CurrencyFormatter accepts arbitrary three-letter codes. Query the OS currency
        // identifiers first, without a copied currency list or reflection over WinRT types.
        if (
            code.Length != 3
            || code.Any(character => character is < 'A' or > 'Z')
            || !ApiInformation.IsPropertyPresent("Windows.Globalization.CurrencyIdentifiers", code)
        )
        {
            throw new ArgumentException("Currency code is not recognized", nameof(code));
        }
        var formatter = languages is null
            ? new CurrencyFormatter(code)
            : new CurrencyFormatter(code, languages, geographicRegion);
        formatter.Mode = CurrencyFormatterMode.UseSymbol;
        formatter.NumeralSystem = "Latn";
        formatter.IntegerDigits = 1;
        formatter.FractionDigits = 0;
        formatter.IsGrouped = false;
        formatter.IsDecimalPointAlwaysDisplayed = false;
        // WinRT exposes formatted currency, not its symbol. Format one known digit with
        // no decimal/grouping, remove that digit, and retain the OS's symbol/code fallback.
        var formatted = formatter.FormatUInt(0);
        var digit = formatted.IndexOf('0');
        if (digit < 0 || formatted.LastIndexOf('0') != digit)
        {
            throw new InvalidOperationException("Windows returned an unexpected currency format.");
        }
        var symbol = formatted.Remove(digit, 1);
        var start = 0;
        var end = symbol.Length;
        while (start < end && IsPadding(symbol[start]))
        {
            start++;
        }
        while (end > start && IsPadding(symbol[end - 1]))
        {
            end--;
        }
        if (start == end)
        {
            throw new InvalidOperationException("Windows returned an empty currency symbol.");
        }
        return symbol[start..end];
    }

    private static bool IsPadding(char value) =>
        char.IsWhiteSpace(value) || char.GetUnicodeCategory(value) == UnicodeCategory.Format;
}
#endif
