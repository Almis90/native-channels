using System.Runtime.InteropServices;

namespace UnoSamples;

// Query the real COM UIA client on an MTA worker so the owning UI thread can
// service provider requests. This never searches or acts on other applications.
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class PdfAccessibilityProbe
{
    public static Task VerifyAsync(nint window, int page, int count, bool focused) =>
        Task.Run(() =>
        {
            var automation = (IUiaClient)
                Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"))!)!;
            IUiaElement? element = null;
            try
            {
                element = automation.ElementFromHandle(window);
                var name = (string)element.GetCurrentPropertyValue(30005);
                if (!name.Contains($"page {page} of {count}", StringComparison.Ordinal))
                    throw new InvalidOperationException("PDF accessibility name: " + name);
                if ((int)element.GetCurrentPropertyValue(30003) != 50033)
                    throw new InvalidOperationException("PDF accessibility role must be Pane.");
                if (
                    !(bool)element.GetCurrentPropertyValue(30009)
                    || (bool)element.GetCurrentPropertyValue(30008) != focused
                )
                    throw new InvalidOperationException("PDF accessibility focus state mismatch.");
            }
            finally
            {
                if (element is not null)
                    Marshal.ReleaseComObject(element);
                Marshal.ReleaseComObject(automation);
            }
        });

    // Only the leading vtable slots needed by this test are declared.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUiaClient
    {
        void CompareElements(nint first, nint second, out int same);
        void CompareRuntimeIds(nint first, nint second, out int same);
        IUiaElement GetRootElement();
        IUiaElement ElementFromHandle(nint window);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUiaElement
    {
        void SetFocus();
        void GetRuntimeId(out nint result);
        void FindFirst(int scope, nint condition, out nint result);
        void FindAll(int scope, nint condition, out nint result);
        void FindFirstBuildCache(int scope, nint condition, nint cache, out nint result);
        void FindAllBuildCache(int scope, nint condition, nint cache, out nint result);
        void BuildUpdatedCache(nint cache, out nint result);

        [return: MarshalAs(UnmanagedType.Struct)]
        object GetCurrentPropertyValue(int propertyId);
    }
}
