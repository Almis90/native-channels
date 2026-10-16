#if WINDOWS
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NativeChannels;

namespace Plugin.Maui.VolumeMonitor;

internal sealed class WindowsVolumeMonitorModule : IWindowsChannelModule
{
    private EventChannel? _channel;
    private readonly CancellationTokenSource _closed = new();

    public void Register(IChannelService channels)
    {
        _channel = channels.GetEventChannel("plugin.maui.volume");
        _channel.SetStreamHandler(ListenAsync);
    }

    private async IAsyncEnumerable<object?> ListenAsync(
        object? arguments,
        [EnumeratorCancellation] CancellationToken cancellation
    )
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closed.Token);
        using var observer = CreateObserver();
        await foreach (var value in observer.ListenAsync(linked.Token).ConfigureAwait(false))
            yield return value;
    }

    private static WindowsVolumeObserver CreateObserver()
    {
        try
        {
            return new WindowsVolumeObserver();
        }
        catch (COMException error)
        {
            throw new ChannelException(
                "volume_unavailable",
                "Cannot observe Windows output volume.",
                error.HResult,
                "windows"
            );
        }
    }

    public void Dispose()
    {
        _closed.Cancel();
        _channel?.Dispose();
    }
}

// Each enumeration owns its COM interfaces and callbacks. Notifications only enqueue signals;
// registration, COM release and device changes happen outside the native callback stack.
internal sealed class WindowsVolumeObserver : IDisposable
{
    private readonly Channel<bool> _signals = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        }
    );
    private readonly CoreAudio.DeviceEnumerator _enumerator;
    private readonly Notifications _notifications;
    private CoreAudio.Device? _device;
    private CoreAudio.EndpointVolume? _volume;
    private string? _deviceId;
    private bool _registered;
    private bool _disposed;

    public WindowsVolumeObserver()
    {
        _enumerator = (CoreAudio.DeviceEnumerator)
            Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
        _notifications = new(_signals.Writer);
        try
        {
            Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_notifications));
            _registered = true;
            Refresh();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public async IAsyncEnumerable<double> ListenAsync([EnumeratorCancellation] CancellationToken cancellation)
    {
        var previous = Read();
        yield return previous;
        await foreach (var signal in _signals.Reader.ReadAllAsync(cancellation).ConfigureAwait(false))
        {
            Refresh();
            var value = Read();
            if (value != previous)
            {
                previous = value;
                yield return value;
            }
        }
    }

    private void Refresh()
    {
        CoreAudio.Device? device = null;
        try
        {
            Marshal.ThrowExceptionForHR(_enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            Marshal.ThrowExceptionForHR(device.GetId(out var id));
            if (id == _deviceId)
                return;
            ReleaseEndpoint();
            _device = device;
            device = null;
            var iid = typeof(CoreAudio.EndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(_device.Activate(ref iid, 23, 0, out var endpoint));
            _volume = (CoreAudio.EndpointVolume)endpoint;
            Marshal.ThrowExceptionForHR(_volume.RegisterControlChangeNotify(_notifications));
            _deviceId = id;
        }
        catch (COMException error)
        {
            throw new ChannelException(
                "volume_unavailable",
                "The default Windows output device is unavailable.",
                error.HResult,
                "windows"
            );
        }
        finally
        {
            // GetDefaultAudioEndpoint may return the same RCW as the owned device.
            if (device is not null)
                Marshal.ReleaseComObject(device);
        }
    }

    private double Read()
    {
        var result = _volume!.GetMasterVolumeLevelScalar(out var value);
        if (result < 0)
            throw new ChannelException("volume_unavailable", "Cannot read Windows output volume.", result, "windows");
        return Math.Clamp((double)value, 0, 1);
    }

    private void ReleaseEndpoint()
    {
        if (_volume is not null)
        {
            if (_deviceId is not null)
                _volume.UnregisterControlChangeNotify(_notifications);
            Marshal.ReleaseComObject(_volume);
            _volume = null;
        }
        if (_device is not null)
            Marshal.ReleaseComObject(_device);
        _device = null;
        _deviceId = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_registered)
            _enumerator.UnregisterEndpointNotificationCallback(_notifications);
        ReleaseEndpoint();
        Marshal.ReleaseComObject(_enumerator);
        _signals.Writer.TryComplete();
        GC.KeepAlive(_notifications);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    private sealed class Notifications(ChannelWriter<bool> signals) : CoreAudio.VolumeCallback, CoreAudio.DeviceCallback
    {
        public int OnNotify(nint data)
        {
            signals.TryWrite(true);
            return 0;
        }

        public int OnDeviceStateChanged(string id, uint state)
        {
            signals.TryWrite(true);
            return 0;
        }

        public int OnDeviceAdded(string id)
        {
            signals.TryWrite(true);
            return 0;
        }

        public int OnDeviceRemoved(string id)
        {
            signals.TryWrite(true);
            return 0;
        }

        public int OnDefaultDeviceChanged(int flow, int role, string? id)
        {
            if (flow == 0 && role == 1)
                signals.TryWrite(true);
            return 0;
        }

        public int OnPropertyValueChanged(string id, CoreAudio.PropertyKey key) => 0;
    }
}
#endif
