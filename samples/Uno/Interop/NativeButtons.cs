using System.Runtime.InteropServices;
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

    public async Task VerifyAsync()
    {
        await WaitAsync(
            () =>
                _adapters.Count == 2
                && _adapters.All(a =>
                    Win32Probe.IsAttached(a.Handle) && (GetWindowLongW(a.Handle, -16) & 0x10000000) != 0
                ),
            "native attachment and show styles"
        );
        Check(_adapters.Count == 2, "two adapters created");
        var first = _adapters[0];
        var second = _adapters[1];
        Check(first.Handle != second.Handle, "independent native handles");
        Check(first.InstanceId != second.InstanceId, "independent channel identities");
        Check(Win32Probe.IsAttached(first.Handle) && Win32Probe.IsAttached(second.Handle), "native parenting");
        Check(
            (GetWindowLongW(first.Handle, -16) & 0x10000000) != 0
                && (GetWindowLongW(second.Handle, -16) & 0x10000000) != 0,
            "Uno shows both child windows"
        );
        Check(
            GetWindowRect(first.Handle, out var before) && before.Right > before.Left && before.Bottom > before.Top,
            "nonempty first native bounds"
        );
        Check(
            GetWindowRect(second.Handle, out var other) && other.Top >= before.Bottom,
            "separate native layout bounds"
        );
        _first.Width = 200;
        Panel.UpdateLayout();
        await WaitAsync(
            () =>
                GetWindowRect(first.Handle, out var bounds) && bounds.Right - bounds.Left != before.Right - before.Left,
            "native resize"
        );
        Check(
            GetWindowRect(first.Handle, out var resized) && resized.Right - resized.Left != before.Right - before.Left,
            "native control follows Uno resize"
        );
        Check(
            GetWindowRect(second.Handle, out var unchanged)
                && unchanged.Right - unchanged.Left == other.Right - other.Left,
            "resizing first control preserves second bounds"
        );
        Check(await _first.Channel!.InvokeAsync<bool>("attached"), "per-view channel reads native state");
        // Deliver input messages directly to these owned controls, without moving
        // the user's pointer or injecting keys into the desktop input queue.
        var previousFocus = GetFocus();
        try
        {
            SetFocus(first.Handle);
            Check(GetFocus() == first.Handle, "first native focus");
            SendMessageW(first.Handle, 0x0201, 1, 0x000A000A); // WM_LBUTTONDOWN
            SendMessageW(first.Handle, 0x0202, 0, 0x000A000A); // WM_LBUTTONUP
            Check(SendMessageW(first.Handle, 0x00F0, 0, 0) == 1, "native mouse-message toggle");
            Check(SendMessageW(second.Handle, 0x00F0, 0, 0) == 0, "second control unaffected");
            SetFocus(second.Handle);
            Check(GetFocus() == second.Handle, "second native focus");
            SendMessageW(second.Handle, 0x0100, 0x20, 0); // WM_KEYDOWN / space
            SendMessageW(second.Handle, 0x0101, 0x20, 0); // WM_KEYUP / space
            Check(SendMessageW(second.Handle, 0x00F0, 0, 0) == 1, "native keyboard-message toggle");
        }
        finally
        {
            SetFocus(previousFocus);
        }
        var oldChannel = _first.Channel!;
        _first.Reconnect();
        await WaitAsync(
            () => _adapters.Count == 3 && Win32Probe.IsAttached(_adapters[2].Handle),
            "reconnected attachment"
        );
        Check(first.Destroyed && !IsWindow(first.Handle), "old native handle destroyed");
        Check(_first.InstanceId != first.InstanceId, "new view identity");
        Check(
            Win32Probe.IsAttached(_adapters[2].Handle) && Win32Probe.IsAttached(second.Handle),
            "reconnect keeps both views attached"
        );
        Check(SendMessageW(second.Handle, 0x00F0, 0, 0) == 1, "reconnect preserves second control state");
        try
        {
            await oldChannel.InvokeAsync("stale");
            throw new InvalidOperationException("Stale channel still usable");
        }
        catch (ObjectDisposedException) { }
        await DisposeAsync();
        Check(_adapters.All(a => a.Destroyed && !IsWindow(a.Handle)), "all native handles destroyed");
    }

    private static void Check(bool condition, string operation)
    {
        if (!condition)
            throw new InvalidOperationException("Windows native view check failed: " + operation);
    }

    private static async Task WaitAsync(Func<bool> ready, string operation)
    {
        var timeout = System.Diagnostics.Stopwatch.StartNew();
        while (!ready())
        {
            Check(timeout.Elapsed < TimeSpan.FromSeconds(5), operation + " timed out");
            await Task.Delay(25);
        }
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
    private static extern bool GetWindowRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLongW(nint window, int index);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nint wparam, nint lparam);

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
            Handle = Win32Probe.CreateButton(toggle: true);
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
