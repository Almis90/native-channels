#if WINDOWS
using System.Drawing;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using NativeChannels;
using Plugin.Maui.PdfViewer;

namespace Plugin.Avalonia.PdfViewer;

// A Win32 child window renders Windows.Data.Pdf pages without a WinUI/MAUI host.
internal sealed class WindowsPdfView : IAvaloniaNativeView
{
    private readonly ChannelService _channels;
    private readonly CancellationTokenSource _stopping = new();
    private readonly WndProc _procedure;
    private readonly nint _original;
    private WindowsPdfDocument? _document;
    private Bitmap? _bitmap;
    private int _page;
    private double _zoom = 1;
    private float _panX,
        _panY;
    private int _mouseX,
        _mouseY;
    private bool _closed,
        _busy;
    private TaskCompletionSource? _operation;
    public IPlatformHandle Handle { get; }
    public MethodChannel Channel { get; }
    public string InstanceId { get; }

    public WindowsPdfView(IPlatformHandle parent, string identity)
    {
        InstanceId = identity;
        var transport = new NativeChannelTransport();
        _channels = new(new BinaryMessenger(transport));
        var name = "nativechannels.view." + identity;
        Channel = _channels.GetMethodChannel(name, new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread });
        transport
            .WindowsChannels.GetMethodChannel(name, new() { HandlerDispatcher = AvaloniaChannelDispatchers.UIThread })
            .SetMethodCallHandler(Invoke);
        var window = CreateWindowExW(0, "STATIC", "PDF", 0x50000000, 0, 0, 1, 1, parent.Handle, 0, 0, 0);
        if (window == 0)
        {
            _ = _channels.DisposeAsync();
            throw new System.ComponentModel.Win32Exception();
        }
        Handle = new PlatformHandle(window, "HWND");
        _procedure = Procedure;
        _original = SetWindowLongPtrW(window, -4, Marshal.GetFunctionPointerForDelegate(_procedure));
        if (_original == 0)
        {
            DestroyWindow(window);
            _ = _channels.DisposeAsync();
            throw new System.ComponentModel.Win32Exception();
        }
    }

    private object State() =>
        new Dictionary<string, object?>
        {
            ["instanceId"] = InstanceId,
            ["page"] = _page,
            ["pageCount"] = _document?.PageCount ?? 0,
            ["zoom"] = _zoom,
        };

    private async ValueTask<object?> Invoke(MethodCall call, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (_busy)
            throw new ChannelException("pdf_busy", "Wait for the current PDF operation.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _stopping.Token);
        var token = linked.Token;
        _busy = true;
        var completion = _operation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            switch (call.Method)
            {
                case "open":
                    var next = await WindowsPdfDocument.OpenAsync(
                        call.Arguments as string ?? throw new ArgumentException("Expected a file path."),
                        token
                    );
                    try
                    {
                        var image = await Render(next, 1, token);
                        _document?.Dispose();
                        _document = next;
                        next = null!;
                        _bitmap?.Dispose();
                        _bitmap = image;
                        _page = 1;
                        _zoom = 1;
                        _panX = _panY = 0;
                    }
                    finally
                    {
                        next?.Dispose();
                    }
                    break;
                case "goToPage":
                    if (_document is null)
                        throw new ChannelException("document_not_open", "Open a PDF first.");
                    var page = Convert.ToInt32(call.Arguments);
                    var bitmap = await Render(_document, page, token);
                    _bitmap?.Dispose();
                    _bitmap = bitmap;
                    _page = page;
                    _panX = _panY = 0;
                    break;
                case "setZoom":
                    var zoom = Convert.ToDouble(call.Arguments);
                    if (!double.IsFinite(zoom) || zoom < 1 || zoom > 4)
                        throw new ChannelException("invalid_zoom", "Zoom must be between 1 and 4.");
                    if (_document is null)
                        throw new ChannelException("document_not_open", "Open a PDF first.");
                    _zoom = zoom;
                    break;
                case "getState":
                    return State();
                default:
                    throw new MethodNotImplementedException(call.Method);
            }
            InvalidateRect(Handle.Handle, 0, false);
            return State();
        }
        finally
        {
            _busy = false;
            completion.TrySetResult();
        }
    }

    private async Task<Bitmap> Render(WindowsPdfDocument document, int page, CancellationToken token)
    {
        using var rendered = await document.RenderAsync(page, token);
        using var stream = rendered.AsStreamForRead();
        using var image = Image.FromStream(stream);
        token.ThrowIfCancellationRequested();
        return new Bitmap(image);
    }

    private nint Procedure(nint hwnd, uint message, nuint wparam, nint lparam)
    {
        if (message == 0x0201) // Drag the magnified page with the left mouse button.
        {
            _mouseX = (short)(long)lparam;
            _mouseY = (short)((long)lparam >> 16);
            SetCapture(hwnd);
        }
        if (message == 0x0202)
            ReleaseCapture();
        if (message == 0x0200 && (wparam & 1) != 0)
        {
            var x = (short)(long)lparam;
            var y = (short)((long)lparam >> 16);
            _panX += x - _mouseX;
            _panY += y - _mouseY;
            _mouseX = x;
            _mouseY = y;
            InvalidateRect(hwnd, 0, false);
        }
        if (message == 0x020A)
        {
            var delta = (short)(wparam >> 16) / 120f * 48;
            if ((wparam & 4) != 0)
                _panX += delta;
            else
                _panY += delta;
            InvalidateRect(hwnd, 0, false);
            return 0;
        }
        if (message == 0x0005)
            InvalidateRect(hwnd, 0, false);
        if (message == 0x000F)
        {
            var hdc = BeginPaint(hwnd, out var paint);
            try
            {
                using var graphics = Graphics.FromHdc(hdc);
                graphics.Clear(Color.FromArgb(226, 232, 240));
                if (_bitmap is { } image)
                {
                    GetClientRect(hwnd, out var rect);
                    if (rect.Right <= 0 || rect.Bottom <= 0)
                        return 0;
                    var scale = Math.Min((double)rect.Right / image.Width, (double)rect.Bottom / image.Height) * _zoom;
                    var w = (float)(image.Width * scale);
                    var h = (float)(image.Height * scale);
                    var maxX = Math.Max(0, (w - rect.Right) / 2);
                    var maxY = Math.Max(0, (h - rect.Bottom) / 2);
                    _panX = Math.Clamp(_panX, -maxX, maxX);
                    _panY = Math.Clamp(_panY, -maxY, maxY);
                    graphics.DrawImage(image, (rect.Right - w) / 2 + _panX, (rect.Bottom - h) / 2 + _panY, w, h);
                }
            }
            catch (Exception error)
            {
                // Exceptions must not cross the unmanaged window procedure boundary.
                System.Diagnostics.Trace.TraceError(error.ToString());
            }
            finally
            {
                EndPaint(hwnd, ref paint);
            }
            return 0;
        }
        return CallWindowProcW(_original, hwnd, message, wparam, lparam);
    }

    public void Destroy()
    {
        if (_closed)
            return;
        _closed = true;
        _stopping.Cancel();
        SetWindowLongPtrW(Handle.Handle, -4, _original);
        DestroyWindow(Handle.Handle);
        _bitmap?.Dispose();
        _bitmap = null;
        // In-flight WinRT operations hold the document until cancellation has completed.
    }

    public async ValueTask DisposeAsync()
    {
        Destroy();
        if (_operation is not null)
            await _operation.Task;
        await _channels.DisposeAsync();
        _document?.Dispose();
        _document = null;
        _stopping.Dispose();
        GC.KeepAlive(_procedure);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Paint
    {
        public nint Hdc;
        public int Erase;
        public Rect Rect;
        public int Restore,
            IncUpdate;
        public fixed byte Reserved[32];
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hwnd, uint message, nuint wparam, nint lparam);

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint exStyle,
        string name,
        string title,
        uint style,
        int x,
        int y,
        int w,
        int h,
        nint parent,
        nint menu,
        nint instance,
        nint param
    );

    [DllImport("user32", SetLastError = true)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32")]
    private static extern nint CallWindowProcW(nint proc, nint window, uint message, nuint wparam, nint lparam);

    [DllImport("user32")]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32")]
    private static extern nint SetCapture(nint window);

    [DllImport("user32")]
    private static extern bool ReleaseCapture();

    [DllImport("user32")]
    private static extern bool InvalidateRect(nint window, nint rect, bool erase);

    [DllImport("user32")]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32")]
    private static extern nint BeginPaint(nint window, out Paint paint);

    [DllImport("user32")]
    private static extern bool EndPaint(nint window, ref Paint paint);
}
#endif
