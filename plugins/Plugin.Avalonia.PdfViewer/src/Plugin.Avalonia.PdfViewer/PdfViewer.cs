using Avalonia.Controls;
using Avalonia.Threading;
using NativeChannels;

namespace Plugin.Avalonia.PdfViewer;

public sealed record PdfState(string InstanceId, int Page, int PageCount, double Zoom);

public sealed class PdfViewer : ContentControl
{
    private readonly AvaloniaChannelView _native;
    private IDisposable? _callback;
    public PdfState? State { get; private set; }
    public bool IsConnected => _native.Channel is not null;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<PdfState>? StateChanged;

    public PdfViewer()
    {
        _native = new(PdfPlatform.Create);
        _native.Connected += (_, _) =>
        {
            var channel = _native.Channel!;
            var identity = _native.InstanceId!;
            _callback = channel.SetMethodCallHandler(
                (call, _) =>
                {
                    if (call.Method != "stateChanged")
                        throw new MethodNotImplementedException(call.Method);
                    if (ReferenceEquals(channel, _native.Channel))
                    {
                        State = Parse(call.Arguments, identity);
                        StateChanged?.Invoke(this, State);
                    }
                    return ValueTask.FromResult<object?>(null);
                }
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

    public void Reconnect() => _native.Reconnect();

    public Task DisconnectAsync() => _native.DisconnectAsync();

    private async Task<PdfState> Invoke(string method, object? value, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
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
            throw new ChannelSerializationException("Invalid native PDF state.");
        return result;
    }
}
