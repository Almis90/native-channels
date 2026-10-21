using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeChannels;

namespace Plugin.Uno.PdfViewer;

public sealed record PdfState(string InstanceId, int Page, int PageCount, double Zoom);

public sealed partial class PdfViewer : ContentControl, IAsyncDisposable
{
    public PdfViewer()
        : this(CreateNativeView) { }

    public static IUnoNativeView CreateNativeView(string instanceId)
    {
#if BROWSER
        return new BrowserPdfView(instanceId);
#elif UNO_LINUX_PDF
        return new UnoLinuxNativeView(
            PdfLinux.Backend == LinuxPdfBackend.Qt ? "nc_pdf_qt" : "nc_pdf_gtk",
            typeof(PdfViewer).Assembly,
            instanceId
        );
#elif IOS || ANDROID
        return new MobilePdfView(instanceId);
#else
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            throw new PlatformNotSupportedException(
                "The bundled PDF adapter requires Windows 10 1809 or later. Supply a custom native factory on other platforms."
            );
        return CreateWindowsView(instanceId);
#endif
    }

#if !IOS && !ANDROID && !UNO_LINUX_PDF && !BROWSER
    // Keep Windows projection types out of the JIT path on other Uno backends.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.17763")]
    private static IUnoNativeView CreateWindowsView(string instanceId) => new WindowsPdfView(instanceId);
#endif

    private readonly UnoChannelView _native;
    private IDisposable? _callback;
    public PdfState? State { get; private set; }
    public bool IsConnected => _native.Channel is not null;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<PdfState>? StateChanged;

    public PdfViewer(Func<string, IUnoNativeView> create)
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _native = new(create)
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        _native.Connected += (_, _) =>
        {
            var dispatcher = new UnoChannelDispatcher(DispatcherQueue);
            var channel = _native.Channel!;
            var identity = _native.InstanceId!;
            _callback = channel.SetMethodCallHandler(
                (call, token) =>
                    dispatcher.DispatchAsync(
                        () =>
                        {
                            if (call.Method != "stateChanged")
                                throw new MethodNotImplementedException(call.Method);
                            if (ReferenceEquals(channel, _native.Channel))
                            {
                                State = Parse(call.Arguments, identity);
                                StateChanged?.Invoke(this, State);
                            }
                            return ValueTask.FromResult<object?>(null);
                        },
                        token
                    )
            );
            Connected?.Invoke(this, EventArgs.Empty);
        };
        _native.Disconnected += (_, _) =>
        {
            _callback?.Dispose();
            _callback = null;
            State = null;
            Disconnected?.Invoke(this, EventArgs.Empty);
        };
        Content = _native;
    }

    public Task<PdfState> OpenAsync(string path, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Use an absolute local file path.", nameof(path));
        return Invoke("open", path, token);
    }

    public Task<PdfState> OpenBytesAsync(byte[] pdf, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (!OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("Use a local file on native platforms.");
        if (pdf.Length == 0 || pdf.Length > 8 * 1024 * 1024)
            throw new ArgumentException("Select a PDF up to 8 MiB.", nameof(pdf));
        return Invoke("openBytes", pdf, token);
    }

    public Task<PdfState> GoToPageAsync(int page, CancellationToken token = default) =>
        page < 1 ? throw new ArgumentOutOfRangeException(nameof(page)) : Invoke("goToPage", page, token);

    public Task<PdfState> SetZoomAsync(double zoom, CancellationToken token = default) =>
        !double.IsFinite(zoom) || zoom < 1 || zoom > 4
            ? throw new ArgumentOutOfRangeException(nameof(zoom))
            : Invoke("setZoom", zoom, token);

    public Task<PdfState> GetStateAsync(CancellationToken token = default) => Invoke("getState", null, token);

    public ValueTask DisposeAsync() => _native.DisposeAsync();

    public void Reconnect() => _native.Reconnect();

    public Task DisconnectAsync() => _native.DisconnectAsync();

    private async Task<PdfState> Invoke(string method, object? value, CancellationToken token)
    {
        new UnoChannelDispatcher(DispatcherQueue).VerifyAccess();
        var channel =
            _native.Channel ?? throw new InvalidOperationException("Wait for Connected before opening a document.");
        var identity = _native.InstanceId!;
        var reply = await channel.InvokeAsync<object>(method, value, token);
        if (!ReferenceEquals(channel, _native.Channel))
            throw new ObjectDisposedException(nameof(PdfViewer));
        State = Parse(reply, identity);
        StateChanged?.Invoke(this, State);
        return State;
    }

    private static PdfState Parse(object? value, string identity)
    {
        var state = new MethodCall("state", value);
        var result = new PdfState(
            state.GetArgument<string>("instanceId")!,
            state.GetArgument<int>("page"),
            state.GetArgument<int>("pageCount"),
            state.GetArgument<double>("zoom")
        );
        if (
            result.InstanceId != identity
            || result.PageCount < 0
            || result.Page < 0
            || result.Page > result.PageCount
            || result.PageCount > 0 && result.Page == 0
            || !double.IsFinite(result.Zoom)
            || result.Zoom < 0.99
            || result.Zoom > 4.01
        )
            throw new ChannelSerializationException($"Invalid native PDF state: {result}.");
        return result;
    }
}
