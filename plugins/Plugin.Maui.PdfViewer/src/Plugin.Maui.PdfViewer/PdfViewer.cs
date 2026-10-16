using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using NativeChannels;

namespace Plugin.Maui.PdfViewer;

/// <summary>State of one native document. Pages are one-based; zero means no open document.</summary>
public sealed record PdfState(string InstanceId, int Page, int PageCount, double Zoom);

/// <summary>A native PDF surface with a private method channel for each connection.</summary>
public sealed class PdfViewer : ContentView
{
    private readonly PlatformChannelView _native = new() { FactoryName = "plugin.pdf" };
    private IDisposable? _registration;

    public PdfViewer()
    {
        _native.Connected += OnConnected;
        _native.Disconnected += (_, _) =>
        {
            _registration?.Dispose();
            _registration = null;
            State = null;
            Disconnected?.Invoke(this, EventArgs.Empty);
        };
        Content = _native;
    }

    public bool IsConnected => _native.Channel is not null;
    public PdfState? State { get; private set; }
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<PdfState>? StateChanged;

    /// <summary>Opens a readable local PDF. Copy MAUI assets to a local file first.</summary>
    public Task<PdfState> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
            throw new ArgumentException("Use an absolute local file path.", nameof(path));
        return InvokeAsync("open", path, cancellationToken);
    }

    public Task<PdfState> GoToPageAsync(int page, CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page));
        return InvokeAsync("goToPage", page, cancellationToken);
    }

    /// <summary>Zoom relative to fitting a page: 1 through 4.</summary>
    public Task<PdfState> SetZoomAsync(double zoom, CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(zoom) || zoom < 1 || zoom > 4)
            throw new ArgumentOutOfRangeException(nameof(zoom));
        return InvokeAsync("setZoom", zoom, cancellationToken);
    }

    public Task<PdfState> GetStateAsync(CancellationToken cancellationToken = default) =>
        InvokeAsync("getState", null, cancellationToken);

    /// <summary>Discards this document and creates a new native view/channel. Reopen after Connected.</summary>
    public void Reconnect() => _native.Reconnect();

    public Task DisconnectAsync() => _native.DisconnectAsync();

    private void OnConnected(object? sender, EventArgs args)
    {
        var channel = _native.Channel!;
        var instanceId = _native.InstanceId!;
        _registration = channel.SetMethodCallHandler(
            (call, _) =>
            {
                if (call.Method != "stateChanged")
                    throw new MethodNotImplementedException();
                if (_native.Channel != channel)
                    return ValueTask.FromResult<object?>(null);
                var state = Parse(call.Arguments, instanceId);
                State = state;
                StateChanged?.Invoke(this, state);
                return ValueTask.FromResult<object?>(null);
            }
        );
        Connected?.Invoke(this, EventArgs.Empty);
    }

    private async Task<PdfState> InvokeAsync(string method, object? arguments, CancellationToken token)
    {
        if (!MainThread.IsMainThread)
            throw new InvalidOperationException("PDF view operations require the UI thread.");
        var channel =
            _native.Channel ?? throw new InvalidOperationException("Wait for the PDF view's Connected event.");
        var instanceId = _native.InstanceId!;
        var value = await channel.InvokeAsync<object>(method, arguments, token);
        if (_native.Channel != channel)
            throw new ObjectDisposedException(nameof(PdfViewer));
        // StateChanged is reserved for the native callback; replies never fabricate an event.
        return Parse(value, instanceId);
    }

    private static PdfState Parse(object? value, string instanceId)
    {
        var call = new MethodCall("state", value);
        var id = call.GetArgument<string>("instanceId");
        var page = call.GetArgument<int>("page");
        var count = call.GetArgument<int>("pageCount");
        var zoom = call.GetArgument<double>("zoom");
        if (
            id != instanceId
            || count < 0
            || page < 0
            || page > count
            || (count > 0 && page == 0)
            || !double.IsFinite(zoom)
            || zoom < 0.99
            || zoom > 4.01
        )
            throw new ChannelSerializationException("Invalid native PDF state.");
        return new(instanceId, page, count, zoom);
    }
}
