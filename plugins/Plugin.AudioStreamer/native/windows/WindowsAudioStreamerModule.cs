#if WINDOWS
using System.Runtime.InteropServices;
using NativeChannels;

namespace Plugin.AudioStreamer;

internal sealed class WindowsAudioStreamerModule : IWindowsChannelModule
{
    private readonly object _gate = new();
    private readonly Queue<Buffer> _buffers = new();
    private MethodChannel? _control;
    private BasicMessageChannel? _data;
    private nint _device;
    private byte[]? _identity;
    private TaskCompletionSource? _lease;
    private bool _paused;
    private bool _disposed;
    private int _buffered;
    private long _played;

    public void Register(IChannelService channels)
    {
        _control = channels.GetMethodChannel(AudioFormat.ControlChannel);
        _control.SetMethodCallHandler(ControlAsync);
        _data = channels.GetBasicMessageChannel(AudioFormat.DataChannel, new() { Codec = BinaryMessageCodec.Instance });
        _data.SetMessageHandler(
            (value, cancellation) =>
            {
                cancellation.ThrowIfCancellationRequested();
                lock (_gate)
                    return ValueTask.FromResult<object?>(new byte[] { Write(value as byte[]) });
            }
        );
    }

    private async ValueTask<object?> ControlAsync(MethodCall call, CancellationToken cancellation)
    {
        if (
            call.Arguments is not IDictionary<string, object?> arguments
            || !arguments.TryGetValue("session", out var value)
            || value is not byte[] identity
            || identity.Length != 16
        )
            throw Error("invalid_arguments", "Supply a 16-byte session identity.");
        Task? lease = null;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            if (call.Method == "open")
            {
                if (_identity is not null)
                    throw Error("audio_busy", "An audio session is already open.");
                var format = new WaveFormat
                {
                    Format = 1,
                    Channels = 1,
                    SamplesPerSecond = AudioFormat.SampleRate,
                    AverageBytesPerSecond = AudioFormat.SampleRate * AudioFormat.BytesPerFrame,
                    BlockAlign = AudioFormat.BytesPerFrame,
                    BitsPerSample = 16,
                };
                Check(Native.waveOutOpen(out _device, uint.MaxValue, ref format, 0, 0, 0));
                _identity = identity.ToArray();
                _lease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _paused = false;
                _played = 0;
                lease = _lease.Task;
            }
            else
            {
                if (_identity is not null && !identity.AsSpan().SequenceEqual(_identity))
                    throw Error("audio_session_mismatch", "This session is no longer current.");
                switch (call.Method)
                {
                    case "status":
                        Reap();
                        return new Dictionary<string, object?>
                        {
                            ["state"] =
                                _identity is null ? "stopped"
                                : _paused ? "paused"
                                : "playing",
                            ["bufferedFrames"] = _buffered,
                            ["capacityFrames"] = AudioFormat.BufferCapacityFrames,
                            ["playedFrames"] = _played,
                        };
                    case "stop":
                        Close();
                        return null;
                    case "pause":
                    case "resume":
                        if (_identity is null)
                            throw Error("audio_closed", "The audio session is closed.");
                        Check(call.Method == "pause" ? Native.waveOutPause(_device) : Native.waveOutRestart(_device));
                        _paused = call.Method == "pause";
                        return null;
                    default:
                        throw new MethodNotImplementedException(call.Method);
                }
            }
        }
        // Never dispose a cancellation registration while holding the lock needed by its callback.
        using var registration = cancellation.Register(() =>
        {
            lock (_gate)
                if (ReferenceEquals(_lease?.Task, lease))
                    Close();
        });
        await lease.ConfigureAwait(false);
        cancellation.ThrowIfCancellationRequested();
        return null;
    }

    private byte Write(byte[]? payload)
    {
        if (
            payload is null
            || payload.Length < 18
            || payload.Length > 16 + AudioFormat.MaxChunkFrames * 2
            || payload.Length % 2 != 0
        )
            return 4;
        if (_identity is null || !payload.AsSpan(0, 16).SequenceEqual(_identity))
            return 3;
        if (_paused)
            return 2;
        Reap();
        var frames = (payload.Length - 16) / 2;
        if (_buffered + frames > AudioFormat.BufferCapacityFrames)
            return 1;
        var buffer = new Buffer(payload, frames);
        try
        {
            Check(Native.waveOutPrepareHeader(_device, buffer.Header, HeaderSize));
            buffer.Prepared = true;
            Check(Native.waveOutWrite(_device, buffer.Header, HeaderSize));
        }
        catch
        {
            buffer.Release(_device);
            throw;
        }
        _buffers.Enqueue(buffer);
        _buffered += frames;
        return 0;
    }

    private void Reap()
    {
        while (_buffers.TryPeek(out var buffer) && (Marshal.PtrToStructure<WaveHeader>(buffer.Header).Flags & 1) != 0)
        {
            buffer.Release(_device);
            _buffers.Dequeue();
            _buffered -= buffer.Frames;
            _played += buffer.Frames;
        }
    }

    private void Close()
    {
        if (_device != 0)
        {
            // Reset returns every queued header before unmanaged storage can be freed.
            Check(Native.waveOutReset(_device));
            while (_buffers.TryPeek(out var buffer))
            {
                buffer.Release(_device);
                _buffers.Dequeue();
            }
            Check(Native.waveOutClose(_device));
            _device = 0;
        }
        _identity = null;
        _buffered = 0;
        _played = 0;
        _lease?.TrySetResult();
        _lease = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            Close();
            _disposed = true;
        }
        _data?.Dispose();
        _control?.Dispose();
    }

    private static ChannelException Error(string code, string message) => new(code, message, platform: "windows");

    private static void Check(uint result)
    {
        if (result != 0)
            throw new ChannelException("audio_device_error", "Windows audio operation failed.", (int)result, "windows");
    }

    private static readonly uint HeaderSize = (uint)Marshal.SizeOf<WaveHeader>();

    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct WaveFormat
    {
        public ushort Format,
            Channels;
        public uint SamplesPerSecond,
            AverageBytesPerSecond;
        public ushort BlockAlign,
            BitsPerSample,
            ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public nint Data;
        public uint BufferLength,
            BytesRecorded;
        public nuint User;
        public uint Flags,
            Loops;
        public nint Next;
        public nuint Reserved;
    }

    private sealed class Buffer
    {
        private readonly nint _data;
        public nint Header { get; }
        public int Frames { get; }
        public bool Prepared { get; set; }

        public Buffer(byte[] payload, int frames)
        {
            Frames = frames;
            _data = Marshal.AllocHGlobal(frames * 2);
            Header = Marshal.AllocHGlobal((int)HeaderSize);
            Marshal.Copy(payload, 16, _data, frames * 2);
            Marshal.StructureToPtr(new WaveHeader { Data = _data, BufferLength = (uint)(frames * 2) }, Header, false);
        }

        public void Release(nint device)
        {
            if (Prepared)
                Check(Native.waveOutUnprepareHeader(device, Header, HeaderSize));
            Marshal.FreeHGlobal(Header);
            Marshal.FreeHGlobal(_data);
        }
    }

    private static class Native
    {
        [DllImport("winmm.dll")]
        internal static extern uint waveOutOpen(
            out nint device,
            uint id,
            ref WaveFormat format,
            nint callback,
            nint instance,
            uint flags
        );

        [DllImport("winmm.dll")]
        internal static extern uint waveOutPrepareHeader(nint device, nint header, uint size);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutUnprepareHeader(nint device, nint header, uint size);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutWrite(nint device, nint header, uint size);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutPause(nint device);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutRestart(nint device);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutReset(nint device);

        [DllImport("winmm.dll")]
        internal static extern uint waveOutClose(nint device);
    }
}
#endif
