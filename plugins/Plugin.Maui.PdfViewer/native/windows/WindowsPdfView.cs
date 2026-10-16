#if WINDOWS
using NativeChannels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Image = Microsoft.UI.Xaml.Controls.Image;
using ScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility;

namespace Plugin.Maui.PdfViewer;

internal sealed class WindowsPdfView : IDisposable
{
    private readonly MethodChannel _channel;
    private readonly string _instanceId;
    private readonly Image _image = new() { Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform };
    private readonly ScrollViewer _view;
    private readonly CancellationTokenSource _closed = new();
    private WindowsPdfDocument? _document;
    private int _page;
    private double _zoom = 1;
    private bool _busy;
    private bool _disposed;

    public static WindowsNativeView Create(WindowsViewContext context)
    {
        var instance = new WindowsPdfView(context);
        return new(instance._view, instance.Dispose);
    }

    private WindowsPdfView(WindowsViewContext context)
    {
        _instanceId = context.InstanceId;
        _view = new ScrollViewer
        {
            Content = _image,
            ZoomMode = ZoomMode.Enabled,
            MinZoomFactor = 1,
            MaxZoomFactor = 4,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _view.SizeChanged += OnSizeChanged;
        _view.ViewChanged += OnViewChanged;
        _channel = context.Channels.GetMethodChannel(
            context.ChannelName,
            new() { HandlerDispatcher = MauiChannelDispatchers.MainThread }
        );
        _channel.SetMethodCallHandler(HandleAsync);
    }

    private Dictionary<string, object?> State() =>
        new()
        {
            ["instanceId"] = _instanceId,
            ["page"] = _page,
            ["pageCount"] = _document?.PageCount ?? 0,
            ["zoom"] = _zoom,
        };

    private async ValueTask<object?> HandleAsync(MethodCall call, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (call.Method == "getState")
            return State();
        if (_busy)
            throw Error("pdf_busy", "A PDF operation is already running.");
        _busy = true;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closed.Token);
        try
        {
            switch (call.Method)
            {
                case "open":
                    WindowsPdfDocument? candidate = null;
                    try
                    {
                        candidate = await WindowsPdfDocument.OpenAsync(call.GetArguments<string>() ?? "", linked.Token);
                        var bitmap = await RenderAsync(candidate, 1, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        _document?.Dispose();
                        _document = candidate;
                        candidate = null;
                        _page = 1;
                        _zoom = 1;
                        _image.Source = bitmap;
                        Fit();
                        _view.ChangeView(0, 0, 1, true);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        throw Error("pdf_open_failed", error.Message);
                    }
                    finally
                    {
                        candidate?.Dispose();
                    }
                    break;
                case "goToPage":
                    RequireDocument();
                    var page = call.GetArguments<int>();
                    if (page < 1 || page > _document!.PageCount)
                        throw Error("invalid_page", "Page is outside this document.");
                    try
                    {
                        var bitmap = await RenderAsync(_document, page, linked.Token);
                        linked.Token.ThrowIfCancellationRequested();
                        _image.Source = bitmap;
                        _page = page;
                        Fit();
                        _view.ChangeView(0, 0, (float)_zoom, true);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception error)
                    {
                        throw Error("pdf_render_failed", error.Message);
                    }
                    break;
                case "setZoom":
                    RequireDocument();
                    var zoom = call.GetArguments<double>();
                    if (!double.IsFinite(zoom) || zoom < 1 || zoom > 4)
                        throw Error("invalid_zoom", "Zoom must be from 1 to 4.");
                    _zoom = zoom;
                    _view.ChangeView(null, null, (float)zoom, true);
                    break;
                default:
                    throw new MethodNotImplementedException(call.Method);
            }
            await NotifyAsync(linked.Token);
            return State();
        }
        finally
        {
            _busy = false;
            if (_disposed)
            {
                _document?.Dispose();
                _document = null;
            }
        }
    }

    private static async Task<BitmapImage> RenderAsync(
        WindowsPdfDocument document,
        int page,
        CancellationToken cancellation
    )
    {
        using var stream = await document.RenderAsync(page, cancellation);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream).AsTask(cancellation);
        return bitmap;
    }

    private void RequireDocument()
    {
        if (_document is null)
            throw Error("document_not_open", "Open a document first.");
    }

    private static ChannelException Error(string code, string message) => new(code, message, platform: "windows");

    private Task NotifyAsync(CancellationToken cancellation) =>
        _channel.InvokeAsync("stateChanged", State(), cancellation);

    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => Fit();

    private void Fit()
    {
        if (_image.Source is not BitmapImage bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0)
            return;
        var width = Math.Max(1, _view.ActualWidth);
        var height = Math.Max(1, _view.ActualHeight);
        var scale = Math.Min(width / bitmap.PixelWidth, height / bitmap.PixelHeight);
        _image.Width = bitmap.PixelWidth * scale;
        _image.Height = bitmap.PixelHeight * scale;
    }

    private async void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs args)
    {
        if (
            _busy
            || _disposed
            || _document is null
            || args.IsIntermediate
            || Math.Abs(_zoom - _view.ZoomFactor) < 0.001
        )
            return;
        _zoom = _view.ZoomFactor;
        try
        {
            await NotifyAsync(_closed.Token);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceError("PDF callback failed: {0}", error);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _closed.Cancel();
        _view.SizeChanged -= OnSizeChanged;
        _view.ViewChanged -= OnViewChanged;
        _channel.Dispose();
        _image.Source = null;
        if (!_busy)
        {
            _document?.Dispose();
            _document = null;
        }
    }
}
#endif
