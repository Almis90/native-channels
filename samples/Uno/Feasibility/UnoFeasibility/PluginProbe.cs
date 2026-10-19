using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;
using Plugin.Currency;
using Plugin.Uno.PdfViewer;
using Plugin.VolumeMonitor;

namespace UnoFeasibility;

internal static class PluginProbe
{
    public static async Task RunAsync(Window window, ContentPresenter surface)
    {
        await using var host = UnoChannelHost.Create(services =>
        {
            services.AddNativeChannels(options => options.AddCurrency().AddAudioStreamer().AddVolumeMonitor());
            services.AddCurrency().AddAudioStreamer().AddVolumeMonitor();
        });
        await using var session = host.CreateSession(window);
        var currency = session.Services.GetRequiredService<ICurrencyService>();
        Require(await currency.GetSymbolAsync("EUR") == "€", "native currency EUR");
        Require(!string.IsNullOrWhiteSpace(await currency.GetSymbolAsync("USD")), "native currency USD");
        try
        {
            await currency.GetSymbolAsync("ZZZ");
            throw new InvalidOperationException("Expected unknown currency error");
        }
        catch (ChannelException error) when (error.Code == "invalid_currency_code") { }

        var volume = session.Services.GetRequiredService<IVolumeMonitor>();
        await using var observer = volume.ListenAsync(session.Stopping).GetAsyncEnumerator();
        Require(await observer.MoveNextAsync() && observer.Current is >= 0 and <= 1, "native output volume");

        var audio = session.Services.GetRequiredService<IAudioStreamer>();
        var playback = await audio.OpenAsync(session.Stopping);
        // Silence validates the native buffer path without producing an audible test tone.
        await playback.WriteAsync(new byte[960], session.Stopping);
        await playback.PauseAsync(session.Stopping);
        Require((await playback.GetStatusAsync()).State == "paused", "foreground audio pause");
        await playback.ResumeAsync(session.Stopping);
        Require((await playback.GetStatusAsync()).State == "playing", "foreground audio resume");

        if (!OperatingSystem.IsMacOS())
            await CheckPdfAsync(surface);
        await session.DisposeAsync();
        Require(playback.IsDisposed && session.Stopping.IsCancellationRequested, "plugin session cleanup");
    }

    private static async Task CheckPdfAsync(ContentPresenter surface)
    {
        var adapters = new List<PdfAdapter>();
        await using var pdf = new PdfViewer(id =>
        {
            var adapter = new PdfAdapter(id);
            adapters.Add(adapter);
            return adapter;
        });
        surface.Content = pdf;
        surface.UpdateLayout();
        await Task.Delay(100);
        Require(pdf.IsConnected, "PDF control attached");
        var state = await pdf.GetStateAsync();
        Require(state.PageCount == 3, "PDF state protocol");
        Require((await pdf.GoToPageAsync(2)).Page == 2, "PDF page command");
        Require((await pdf.SetZoomAsync(2)).Zoom == 2, "PDF zoom command");
        var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        pdf.StateChanged += (_, _) =>
        {
            Require(pdf.DispatcherQueue.HasThreadAccess, "PDF callback UI dispatch");
            notified.TrySetResult();
        };
        await Task.Run(() => adapters[0].NotifyAsync());
        await notified.Task;
        adapters[0].Invalid = true;
        try
        {
            await pdf.GetStateAsync();
            throw new InvalidOperationException("Expected mismatched PDF identity error");
        }
        catch (ChannelSerializationException) { }
        var old = pdf.State!.InstanceId;
        pdf.Reconnect();
        Require((await pdf.GetStateAsync()).InstanceId != old, "PDF reconnect identity");
        await pdf.DisconnectAsync();
        surface.Content = null;
        Require(
            pdf.State is null && adapters.Count == 2 && adapters.All(adapter => adapter.Disposed),
            "PDF adapter cleanup"
        );
    }

    private static void Require(bool value, string operation)
    {
        if (!value)
            throw new InvalidOperationException("Uno plugin probe failed: " + operation);
    }

    // Protocol fixture only: not a PDF renderer and not native embedding evidence.
    private sealed class PdfAdapter : IUnoNativeView
    {
        private readonly ChannelService _managed;
        private readonly ChannelService _native;
        private readonly MethodChannel _peer;
        private int _page = 1;
        private double _zoom = 1;
        public bool Invalid { get; set; }
        public bool Disposed { get; private set; }
        public object NativeElement { get; } = new TextBlock { Text = "PDF protocol fixture" };
        public MethodChannel Channel { get; }
        public string InstanceId { get; }

        public PdfAdapter(string identity)
        {
            InstanceId = identity;
            var pair = InMemoryTransport.CreatePair();
            _managed = new(new BinaryMessenger(pair.Managed));
            _native = new(new BinaryMessenger(pair.Native));
            Channel = _managed.GetMethodChannel("pdf/" + identity);
            _peer = _native.GetMethodChannel(Channel.Name);
            _peer.SetMethodCallHandler(
                (call, _) =>
                {
                    if (call.Method == "goToPage")
                        _page = call.GetArguments<int>();
                    if (call.Method == "setZoom")
                        _zoom = call.GetArguments<double>();
                    return ValueTask.FromResult<object?>(State());
                }
            );
        }

        private object State() =>
            new Dictionary<string, object?>
            {
                ["instanceId"] = Invalid ? "stale" : InstanceId,
                ["page"] = _page,
                ["pageCount"] = 3,
                ["zoom"] = _zoom,
            };

        public Task NotifyAsync() => _peer.InvokeAsync("stateChanged", State());

        public void Destroy() => Channel.Dispose();

        public async ValueTask DisposeAsync()
        {
            await _managed.DisposeAsync();
            await _native.DisposeAsync();
            Disposed = true;
        }
    }
}
