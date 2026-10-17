#if LINUX
using System.Globalization;
using System.Runtime.InteropServices;

namespace Plugin.Maui.Currency;

/// <summary>Queries the system ICU library directly; no managed or copied currency table.</summary>
internal static class LinuxCurrencySymbols
{
    private static readonly Lazy<Icu> Library = new(() => new Icu());

    public static string GetSymbol(string code, string? locale = null)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 3 || code.Any(character => character is < 'A' or > 'Z'))
            throw new ArgumentException("Expected an uppercase three-letter currency code", nameof(code));
        var icu = Library.Value;
        if (icu.NumericCode(code) == 0)
            throw new ArgumentException("Currency code is not recognized", nameof(code));
        var error = 0;
        var pointer = icu.GetName(
            code,
            locale ?? CultureInfo.CurrentCulture.Name.Replace('-', '_'),
            0,
            IntPtr.Zero,
            out var length,
            ref error
        );
        // ICU warnings are negative; only positive UErrorCode values are failures.
        if (error > 0 || pointer == IntPtr.Zero || length <= 0)
            throw new InvalidOperationException($"ICU currency lookup failed (UErrorCode {error}).");
        return Marshal.PtrToStringUni(pointer, length)!;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NumericCodeFunction([MarshalAs(UnmanagedType.LPWStr)] string currency);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetNameFunction(
        [MarshalAs(UnmanagedType.LPWStr)] string currency,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string locale,
        int style,
        IntPtr choiceFormat,
        out int length,
        ref int error
    );

    private sealed class Icu
    {
        public readonly NumericCodeFunction NumericCode;
        public readonly GetNameFunction GetName;

        public Icu()
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("The Linux Currency adapter requires Linux and system ICU.");
            // Distribution runtime packages have versioned SONAMEs; the unversioned name
            // is normally installed only by libicu-dev. Support ICU 60–99 explicitly.
            IntPtr library = IntPtr.Zero;
            if (!NativeLibrary.TryLoad("libicuuc.so", out library))
            {
                for (var version = 99; version >= 60 && library == IntPtr.Zero; version--)
                    NativeLibrary.TryLoad($"libicuuc.so.{version}", out library);
            }
            if (library == IntPtr.Zero)
                throw new DllNotFoundException(
                    "Install the system ICU runtime (for example libicu74 on Ubuntu 24.04). Supported ICU majors: 60–99."
                );
            try
            {
                for (var version = 100; version >= 60; version--)
                {
                    var suffix = version == 100 ? "" : $"_{version}";
                    if (
                        NativeLibrary.TryGetExport(library, "ucurr_getNumericCode" + suffix, out var numeric)
                        && NativeLibrary.TryGetExport(library, "ucurr_getName" + suffix, out var name)
                    )
                    {
                        NumericCode = Marshal.GetDelegateForFunctionPointer<NumericCodeFunction>(numeric);
                        GetName = Marshal.GetDelegateForFunctionPointer<GetNameFunction>(name);
                        // Retain the handle for process lifetime: static delegates and ICU's
                        // returned string pointers must never refer to an unloaded library.
                        return;
                    }
                }
                throw new EntryPointNotFoundException("System ICU has no supported currency entrypoints (ICU 60–99).");
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }
    }
}
#endif
