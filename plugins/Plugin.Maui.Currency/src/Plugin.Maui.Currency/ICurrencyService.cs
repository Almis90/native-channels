namespace Plugin.Maui.Currency;

/// <summary>Looks up currency symbols using the current native display locale.</summary>
public interface ICurrencyService
{
    /// <summary>Returns the native symbol for a three-letter currency code, such as USD or EUR.</summary>
    /// <remarks>Input is trimmed and normalized to uppercase. A symbol can include a country prefix,
    /// or be the currency code itself when the platform has no localized symbol.</remarks>
    /// <exception cref="ArgumentException">The code does not contain three ASCII letters.</exception>
    /// <exception cref="NativeChannels.ChannelException">The native platform does not recognize the code.</exception>
    Task<string> GetSymbolAsync(string currencyCode, CancellationToken cancellationToken = default);
}
