using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using NativeChannels;
using PdfControl = Plugin.Uno.PdfViewer.PdfViewer;

namespace UnoSamples;

[SupportedOSPlatform("windows10.0.17763")]
internal static class NativePdfProbe
{
    public static async Task RunAsync(ContentPresenter surface, string path)
    {
        var views = new List<IUnoNativeView>();
        IUnoNativeView Create(string id)
        {
            var view = PdfControl.CreateNativeView(id);
            views.Add(view);
            return view;
        }
        await using var pdf = new PdfControl(Create) { Width = 480, Height = 360 };
        surface.Content = pdf;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!pdf.IsConnected)
                await Task.Delay(25, timeout.Token);
            var state = await pdf.OpenAsync(path, timeout.Token);
            Require(state.Page == 1 && state.PageCount >= 2, "native document opened");
            var handle = ((global::Uno.UI.NativeElementHosting.Win32NativeWindow)views[0].NativeElement).Hwnd;
            surface.UpdateLayout();
            await Task.Delay(200, timeout.Token);
            Require(GetParent(handle) != 0, "PDF HWND parent");
            Require(
                (GetWindowLongW(GetParent(handle), -16) & 0x02000000) != 0,
                "parent excludes PDF child from software painting"
            );
            Require(GetWindowRgnBox(handle, out _) != 1, "PDF HWND must not be clipped to an empty region");
            Require(GetWindowRect(handle, out var bounds), "PDF screen bounds");
            var hitPoint = Point(bounds.Left + 20, bounds.Top + 20);
            Require(SendMessageW(handle, 0x0084, 0, hitPoint) == 1, "PDF accepts mouse hit testing (HTCLIENT)");
            using var first = Capture(handle);
            Require(HasInk(first), "native renderer produced page pixels");
            var result = Environment.GetEnvironmentVariable("NATIVECHANNELS_UNO_SAMPLE_RESULT");
            if (result is not null)
                first.Save(Path.ChangeExtension(result, ".page1.png"), ImageFormat.Png);
            var firstHash = Hash(first);
            state = await pdf.GoToPageAsync(2, timeout.Token);
            Require(state.Page == 2, "native page navigation");
            using var second = Capture(handle);
            Require(HasInk(second) && Hash(second) != firstHash, "second PDF page pixels differ");
            await pdf.SetZoomAsync(2, timeout.Token);
            using var zoomed = Capture(handle);
            Require(Hash(zoomed) != Hash(second), "native zoom changes painting");
            // Hit testing above exercises the routing prerequisite; these messages
            // separately verify the native gesture handlers and rendered result.
            SendMessageW(handle, 0x0200, 1, Point(120, 140));
            using var noDrag = Capture(handle);
            Require(Hash(noDrag) == Hash(zoomed), "mouse move without owned drag does not pan");
            SendMessageW(handle, 0x0201, 1, Point(120, 100));
            Require(GetCapture() == handle && GetFocus() == handle, "PDF drag owns capture and focus");
            SendMessageW(handle, 0x0200, 1, Point(120, 140));
            SendMessageW(handle, 0x0202, 0, Point(120, 140));
            Require(GetCapture() != handle, "PDF releases drag capture");
            using var dragged = Capture(handle);
            Require(Hash(dragged) != Hash(zoomed), "drag changes native PDF pixels");
            SendMessageW(handle, 0x020A, (nint)(120 << 16), hitPoint);
            using var wheeled = Capture(handle);
            Require(Hash(wheeled) != Hash(dragged), "wheel changes native PDF pixels");
            SendMessageW(handle, 0x020A, (nint)((120 << 16) | 4), hitPoint);
            using var shifted = Capture(handle);
            Require(Hash(shifted) != Hash(wheeled), "shift-wheel pans horizontally");
            SendMessageW(handle, 0x0201, 1, Point(120, 100));
            SendMessageW(handle, 0x001F, 0, 0);
            Require(GetCapture() != handle, "cancel mode releases drag capture");
            SendMessageW(handle, 0x0200, 1, Point(120, 160));
            using var cancelled = Capture(handle);
            Require(Hash(cancelled) == Hash(shifted), "cancelled drag does not move page");
            try
            {
                await pdf.GoToPageAsync(state.PageCount + 1, timeout.Token);
                throw new InvalidOperationException("Invalid page accepted");
            }
            catch (ChannelException) { }
            Require((await pdf.GetStateAsync(timeout.Token)).Page == 2, "invalid page preserves document");
            var invalid = Path.Combine(Path.GetTempPath(), "uno-invalid-" + Guid.NewGuid().ToString("N") + ".pdf");
            try
            {
                await File.WriteAllTextAsync(invalid, "not a PDF", timeout.Token);
                try
                {
                    await pdf.OpenAsync(invalid, timeout.Token);
                    throw new InvalidOperationException("Invalid PDF accepted");
                }
                catch (ChannelException) { }
                Require((await pdf.GetStateAsync(timeout.Token)).Page == 2, "failed open preserves document");
            }
            finally
            {
                File.Delete(invalid);
            }
            var old = views[0].Channel;
            var identity = state.InstanceId;
            pdf.Reconnect();
            Require(!IsWindow(handle), "old PDF HWND destroyed");
            state = await pdf.OpenAsync(path, timeout.Token);
            Require(state.InstanceId != identity && state.Page == 1, "reconnect and reopen");
            try
            {
                await old.InvokeAsync("getState");
                throw new InvalidOperationException("Stale PDF channel accepted");
            }
            catch (ObjectDisposedException) { }
            var next = ((global::Uno.UI.NativeElementHosting.Win32NativeWindow)views[1].NativeElement).Hwnd;
            var pending = pdf.GoToPageAsync(2, timeout.Token);
            await pdf.DisconnectAsync();
            try
            {
                await pending;
                throw new InvalidOperationException("Detached render result accepted");
            }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            Require(!IsWindow(next) && pdf.State is null, "PDF shutdown releases HWND and state");
        }
        finally
        {
            await pdf.DisconnectAsync();
            surface.Content = null;
        }
    }

    [DllImport("user32.dll")]
    private static extern nint GetCapture();

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    private static nint Point(int x, int y) => (nint)((uint)(ushort)x | ((uint)(ushort)y << 16));

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    private static Bitmap Capture(nint window)
    {
        Require(GetClientRect(window, out var rect) && rect.Right > 0 && rect.Bottom > 0, "native PDF bounds");
        var bitmap = new Bitmap(rect.Right, rect.Bottom);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Magenta); // A missing paint handler must not look like page ink.
        var hdc = graphics.GetHdc();
        try
        {
            SendMessageW(window, 0x0318, hdc, 4);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }
        return bitmap;
    }

    private static bool HasInk(Bitmap bitmap)
    {
        var dark = 0;
        for (var y = 0; y < bitmap.Height; y += 3)
        for (var x = 0; x < bitmap.Width; x += 3)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.R < 100 && pixel.G < 100 && pixel.B < 100)
                dark++;
        }
        return dark > 20;
    }

    private static string Hash(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream.ToArray()));
    }

    private static void Require(bool value, string check)
    {
        if (!value)
            throw new InvalidOperationException("Native PDF probe failed: " + check);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left,
            Top,
            Right,
            Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll")]
    private static extern int GetWindowRgnBox(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint wparam, nint lparam);
}
