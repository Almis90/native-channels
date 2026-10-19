using NativeChannels;
using Plugin.Uno.PdfViewer;

namespace UnoFeasibility;

internal static class PdfProtocolProbe
{
    public static async Task RunAsync(ContentPresenter surface)
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
