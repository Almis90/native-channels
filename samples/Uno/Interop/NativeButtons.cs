using NativeChannels;
using UnoFeasibility;

namespace UnoSamples;

// Uses Uno's public HWND wrapper. Attachment is checked separately from channel connectivity.
internal sealed class NativeButtons : IAsyncDisposable
{
    private readonly List<ButtonAdapter> _adapters = [];
    private readonly UnoChannelView _first;
    private readonly UnoChannelView _second;
    public StackPanel Panel { get; } = new() { Spacing = 16 };
    public string AttachmentStatus =>
        _adapters.Count(a => !a.Destroyed) == 2
        && _adapters.Where(a => !a.Destroyed).All(a => Win32Probe.IsAttached(a.Handle))
            ? "Both HWNDs have parents; visibility and input are not yet verified."
            : "Native attachment failed: one or both HWNDs have no parent.";

    public NativeButtons()
    {
        _first = new UnoChannelView(Create) { Height = 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        _second = new UnoChannelView(Create) { Height = 80, HorizontalAlignment = HorizontalAlignment.Stretch };
        Panel.Children.Add(_first);
        Panel.Children.Add(_second);
    }

    private IUnoNativeView Create(string id)
    {
        var adapter = new ButtonAdapter(id);
        _adapters.Add(adapter);
        return adapter;
    }

    public void Reconnect() => _first.Reconnect();

    public async ValueTask DisposeAsync()
    {
        await _first.DisposeAsync();
        await _second.DisposeAsync();
        Panel.Children.Clear();
    }

    private sealed class ButtonAdapter : IUnoNativeView
    {
        private readonly ChannelService _managed;
        private readonly ChannelService _peer;
        public nint Handle { get; }
        public bool Destroyed { get; private set; }
        public object NativeElement { get; }
        public MethodChannel Channel { get; }
        public string InstanceId { get; }

        public ButtonAdapter(string id)
        {
            InstanceId = id;
            Handle = Win32Probe.CreateButton();
            NativeElement = new Uno.UI.NativeElementHosting.Win32NativeWindow(Handle);
            var pair = InMemoryTransport.CreatePair();
            _managed = new(new BinaryMessenger(pair.Managed));
            _peer = new(new BinaryMessenger(pair.Native));
            Channel = _managed.GetMethodChannel("button/" + id);
            _peer
                .GetMethodChannel(Channel.Name)
                .SetMethodCallHandler((_, _) => ValueTask.FromResult<object?>(Win32Probe.IsAttached(Handle)));
        }

        public void Destroy()
        {
            if (Destroyed)
                return;
            Channel.Dispose();
            Win32Probe.DestroyWindow(Handle);
            Destroyed = true;
        }

        public async ValueTask DisposeAsync()
        {
            Destroy();
            await _managed.DisposeAsync();
            await _peer.DisposeAsync();
        }
    }
}
