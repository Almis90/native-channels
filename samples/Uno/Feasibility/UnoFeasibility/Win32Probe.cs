using System.Runtime.InteropServices;

namespace UnoFeasibility;

internal static class Win32Probe
{
    public static nint CreateButton(bool toggle = false)
    {
        var window = CreateWindowExW(0, "BUTTON", "Native Win32 button", 0x80000000, 0, 0, 320, 80, 0, 0, 0, 0);
        if (window == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        // SetParent does not convert WS_POPUP to WS_CHILD. Uno owns reparenting,
        // but the adapter must supply child-window semantics before attachment.
        if (SetWindowLongW(window, -16, 0x40010000 | (toggle ? 3 : 0)) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            DestroyWindow(window);
            throw new System.ComponentModel.Win32Exception(error);
        }
        return window;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);

    public static object Snapshot(nint window) =>
        new
        {
            parent = GetParent(window).ToInt64(),
            ancestor = GetAncestor(window, 1).ToInt64(),
            style = GetWindowLongW(window, -16).ToString("X8"),
        };

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint window, int index);

    public static bool IsAttached(nint window) => GetParent(window) != 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string className,
        string text,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter
    );

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(nint window);
}
