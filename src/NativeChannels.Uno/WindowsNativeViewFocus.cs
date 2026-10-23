using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace NativeChannels;

// One HWND is one tab stop. Compound native controls own navigation inside
// their child HWND tree; this bridge handles the adapter's root window only.
internal sealed class WindowsNativeViewFocus : IDisposable
{
    private readonly UnoChannelView _owner;
    private readonly nint _window;
    private readonly SubclassProc _procedure;
    private bool _synchronizing;
    private bool _pendingFocus;
    private bool _disposed;
    public bool HasFocus => GetFocus() == _window;

    public void Focus()
    {
        if (_disposed)
            return;
        // Check the child's own show state; hidden test hosts still exercise
        // focus without making the application's top-level window visible.
        _pendingFocus = (GetWindowLongW(_window, -16) & 0x10000000) == 0;
        if (!_pendingFocus)
            SetFocus(_window);
    }

    public WindowsNativeViewFocus(UnoChannelView owner, nint window)
    {
        _owner = owner;
        _window = window;
        _procedure = OnMessage;
        if (!SetWindowSubclass(window, _procedure, 1, 0))
            throw new InvalidOperationException("Cannot attach native focus bridge.");
        owner.GotFocus += OnFocus;
    }

    private void OnFocus(object sender, RoutedEventArgs args)
    {
        if (
            !_synchronizing
            && _owner.XamlRoot is { } root
            && ReferenceEquals(FocusManager.GetFocusedElement(root), _owner)
        )
            Focus();
    }

    private nint OnMessage(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data)
    {
        try
        {
            if (message == 0x0018 && wparam != 0 && _pendingFocus) // WM_SHOWWINDOW
                _owner.DispatcherQueue.TryEnqueue(Focus);
            if (message == 0x0007) // WM_SETFOCUS, including mouse focus.
            {
                _synchronizing = true;
                try
                {
                    _owner.Focus(FocusState.Programmatic);
                }
                finally
                {
                    _synchronizing = false;
                }
            }
            if (message == 0x0100 && wparam == 9 && _owner.XamlRoot is { } root) // WM_KEYDOWN / Tab
            {
                var direction =
                    GetKeyState(0x10) < 0 ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
                SetFocus(GetParent(window));
                if (!FocusManager.TryMoveFocus(direction, new FindNextElementOptions { SearchRoot = root.Content }))
                    SetFocus(window);
                return 0;
            }
            if (message == 0x0102 && wparam == 9) // Suppress translated Tab character.
                return 0;
        }
        catch (Exception error)
        {
            // Never unwind a managed exception through the native callback.
            System.Diagnostics.Trace.TraceError(error.ToString());
        }
        return DefSubclassProc(window, message, wparam, lparam);
    }

    public void Dispose()
    {
        _disposed = true;
        _owner.GotFocus -= OnFocus;
        if (GetFocus() == _window)
        {
            SetFocus(GetParent(_window));
            if (_owner.XamlRoot is { } root)
                FocusManager.TryMoveFocus(
                    FocusNavigationDirection.Next,
                    new FindNextElementOptions { SearchRoot = root.Content }
                );
        }
        RemoveWindowSubclass(_window, _procedure, 1);
        GC.KeepAlive(_procedure);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint window, uint message, nuint wparam, nint lparam, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern bool SetWindowSubclass(nint window, SubclassProc procedure, nuint id, nuint data);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc procedure, nuint id);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint window, uint message, nuint wparam, nint lparam);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint window, int index);
}
