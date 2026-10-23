using System.Runtime.InteropServices;

namespace Plugin.Uno.PdfViewer;

// A provider for the raster surface's identity, status and keyboard focus.
// It deliberately does not advertise TextPattern or readable document content.
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class WindowsPdfAccessibility(nint window)
    : IWindowsPdfProvider,
        IWindowsPdfFragment,
        IWindowsPdfFragmentRoot,
        IDisposable
{
    private string _name = "PDF viewer, no document open";
    private bool _closed;
    public int ProviderOptions => 2; // ServerSideProvider

    public IWindowsPdfFragment? Navigate(int direction) => null;

    public int[]? GetRuntimeId() => null; // UIA supplies the HWND identity.

    public IWindowsPdfProvider[]? GetEmbeddedFragmentRoots() => null;

    public IWindowsPdfFragmentRoot FragmentRoot => this;

    public IWindowsPdfFragment? GetFocus() => !_closed && HasFocus() ? this : null;

    public void SetFocus() { } // UIA focuses this HWND before calling the fragment.

    public PdfAutomationRect BoundingRectangle
    {
        get
        {
            if (_closed || !GetWindowRect(window, out var rect))
                return default;
            return new()
            {
                Left = rect.Left,
                Top = rect.Top,
                Width = rect.Right - rect.Left,
                Height = rect.Bottom - rect.Top,
            };
        }
    }

    public IWindowsPdfFragment? ElementProviderFromPoint(double x, double y)
    {
        var rect = BoundingRectangle;
        return !_closed && x >= rect.Left && y >= rect.Top && x < rect.Left + rect.Width && y < rect.Top + rect.Height
            ? this
            : null;
    }

    public object? GetPatternProvider(int patternId) => null;

    public object? GetPropertyValue(int propertyId)
    {
        if (_closed)
            throw new COMException("PDF view disconnected.", unchecked((int)0x80040201));
        return propertyId switch
        {
            30003 => 50033, // Pane, not a Document with unsupported TextPattern.
            30005 => _name,
            30008 => HasFocus(),
            30009 => true,
            30010 => IsWindowEnabled(window),
            30013 =>
                "Rendered PDF page. Use Tab or Shift+Tab to move to document controls. Document text is not exposed by this surface.",
            30016 or 30017 => true,
            _ => null,
        };
    }

    public IWindowsPdfProvider? HostRawElementProvider
    {
        get
        {
            Marshal.ThrowExceptionForHR(UiaHostProviderFromHwnd(window, out var provider));
            return provider;
        }
    }

    public nint GetObject(nuint wparam, nint lparam) => UiaReturnRawElementProvider(window, wparam, lparam, this);

    public void FocusChanged() => UiaRaiseAutomationEvent(this, 20005);

    public void Update(int page, int count, double zoom)
    {
        var oldName = _name;
        _name = $"PDF viewer, page {page} of {count}, zoom {zoom:P0}";
        if (oldName != _name)
            UiaRaiseAutomationPropertyChangedEvent(this, 30005, oldName, _name);
    }

    private bool HasFocus()
    {
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(GetWindowThreadProcessId(window, out _), ref info) && info.Focus == window;
    }

    public void Dispose()
    {
        _closed = true;
        UiaDisconnectProvider(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size,
            Flags;
        public nint Active,
            Focus,
            Capture,
            MenuOwner,
            MoveSize,
            Caret;
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint window);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaHostProviderFromHwnd(nint window, out IWindowsPdfProvider? provider);

    [DllImport("UIAutomationCore.dll")]
    private static extern nint UiaReturnRawElementProvider(
        nint window,
        nuint wparam,
        nint lparam,
        IWindowsPdfProvider provider
    );

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaRaiseAutomationEvent(IWindowsPdfProvider provider, int eventId);

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaRaiseAutomationPropertyChangedEvent(
        IWindowsPdfProvider provider,
        int propertyId,
        [MarshalAs(UnmanagedType.Struct)] object oldValue,
        [MarshalAs(UnmanagedType.Struct)] object newValue
    );

    [DllImport("UIAutomationCore.dll")]
    private static extern int UiaDisconnectProvider(IWindowsPdfProvider provider);
}

[ComVisible(true)]
[Guid("D6DD68D1-86FD-4332-8666-9ABEDEA2D24C")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsPdfProvider
{
    int ProviderOptions { get; }

    [return: MarshalAs(UnmanagedType.IUnknown)]
    object? GetPatternProvider(int patternId);

    [return: MarshalAs(UnmanagedType.Struct)]
    object? GetPropertyValue(int propertyId);
    IWindowsPdfProvider? HostRawElementProvider { get; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PdfAutomationRect
{
    public double Left,
        Top,
        Width,
        Height;
}

[ComVisible(true)]
[Guid("F7063DA8-8359-439C-9297-BBC5299A7D87")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsPdfFragment
{
    IWindowsPdfFragment? Navigate(int direction);

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)]
    int[]? GetRuntimeId();
    PdfAutomationRect BoundingRectangle { get; }

    [return: MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)]
    IWindowsPdfProvider[]? GetEmbeddedFragmentRoots();
    void SetFocus();
    IWindowsPdfFragmentRoot FragmentRoot { get; }
}

[ComVisible(true)]
[Guid("620CE2A5-AB8F-40A9-86CB-DE3C75599B58")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IWindowsPdfFragmentRoot
{
    IWindowsPdfFragment? ElementProviderFromPoint(double x, double y);
    IWindowsPdfFragment? GetFocus();
}
