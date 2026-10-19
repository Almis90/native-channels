#if DESKTOP && !WINDOWS
using System.Runtime.InteropServices;
using System.Text;

namespace Plugin.Currency;

// Uno desktop uses plain .NET and cannot reference the platform-targeted WinRT projection.
// Query the Windows NLS database instead of shipping a second currency table.
internal static class WindowsCurrencySymbols
{
    public static string GetSymbol(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 3 || code.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Currency code is not recognized", nameof(code));
        string? symbol = null;
        LocaleCallback callback = (locale, _, _) =>
        {
            if (Read(locale, 0x15) != code)
                return true;
            symbol = Read(locale, 0x14);
            return false;
        };
        EnumSystemLocalesEx(callback, 0, 0, 0);
        GC.KeepAlive(callback);
        return !string.IsNullOrWhiteSpace(symbol)
            ? symbol
            : throw new ArgumentException("Currency code is not recognized", nameof(code));
    }

    private static string Read(string locale, uint type)
    {
        var buffer = new StringBuilder(128);
        return GetLocaleInfoEx(locale, type, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool LocaleCallback([MarshalAs(UnmanagedType.LPWStr)] string locale, uint flags, nint parameter);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumSystemLocalesEx(LocaleCallback callback, uint flags, nint parameter, nint reserved);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetLocaleInfoEx(string locale, uint type, StringBuilder data, int length);
}
#endif
