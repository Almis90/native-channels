using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NativeChannels;

// Uno's Win32 software renderer blits into the parent DC. Without WS_CLIPCHILDREN
// it overwrites hosted HWNDs even when their own clipping regions are nonempty.
[SupportedOSPlatform("windows")]
internal static class WindowsNativeViewClipping
{
    internal static bool Prepare(nint child)
    {
        var parent = GetParent(child);
        if (parent == 0)
            return false; // Uno has not attached the native element yet.
        const int styleIndex = -16;
        const int clipChildren = 0x02000000;
        Marshal.SetLastPInvokeError(0);
        var style = GetWindowLongW(parent, styleIndex);
        if (style == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception();
        if ((style & clipChildren) == 0)
        {
            Marshal.SetLastPInvokeError(0);
            if (SetWindowLongW(parent, styleIndex, style | clipChildren) == 0 && Marshal.GetLastPInvokeError() != 0)
                throw new Win32Exception();
        }
        // Recover pixels already erased before the parent style was set. Leave
        // the flag on the parent for its lifetime: sibling views share this DC.
        if (!InvalidateRect(child, 0, true))
            throw new Win32Exception();
        return true;
    }

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(nint window, nint rect, bool erase);
}
