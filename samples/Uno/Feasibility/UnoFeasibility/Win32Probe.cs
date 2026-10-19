using System.Runtime.InteropServices;

namespace UnoFeasibility;

internal static class Win32Probe
{
    public static nint CreateButton()
    {
        var window = CreateWindowExW(0, "BUTTON", "Native Win32 button", 0x80000000,
            0, 0, 320, 80, 0, 0, 0, 0);
        if (window == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return window;
    }

    public static bool IsAttached(nint window) => GetParent(window) != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(uint exStyle, string className, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);
}
