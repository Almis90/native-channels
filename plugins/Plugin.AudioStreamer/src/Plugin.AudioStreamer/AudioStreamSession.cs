using NativeChannels;

namespace Plugin.AudioStreamer;

public sealed class AudioStreamSession : IAsyncDisposable
{
    private readonly MethodChannel _control;
    private readonly BasicMessageChannel _data;
    private readonly byte[] _identity = Guid.NewGuid().ToByteArray();
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationTokenSource _leaseCancellation = new();
    private readonly SemaphoreSlim _writer = new(1);
    private readonly object _gate = new();
    private Task _lease = Task.CompletedTask;
    private Task? _dispose;
    private int _disposed;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    private Dictionary<string, object?> Arguments => new() { ["session"] = _identity };

    internal AudioStreamSession(IChannelService channels)
    {
        _control = channels.GetMethodChannel(AudioFormat.ControlChannel);
        _data = channels.GetBasicMessageChannel(AudioFormat.DataChannel, new() { Codec = BinaryMessageCodec.Instance });
    }

    internal async Task InitializeAsync(CancellationToken cancellation)
    {
        // This pending request is a resource lease. Bridge disposal cancels it even if the app forgets to stop.
        _lease = _control.InvokeAsync(
            "open",
            Arguments,
            _leaseCancellation.Token,
            new() { Timeout = Timeout.InfiniteTimeSpan }
        );
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        startup.CancelAfter(TimeSpan.FromSeconds(5));
        while (true)
        {
            await CheckLeaseAsync().ConfigureAwait(false);
            var status = await GetStatusAsync(startup.Token).ConfigureAwait(false);
            if (status.State == "playing")
                return;
            await Task.Delay(10, startup.Token).ConfigureAwait(false);
        }
    }

    /// <summary>Acknowledges native buffering, not audible playback. Full/paused buffers apply backpressure.</summary>
    public async Task WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (
            pcm.IsEmpty
            || pcm.Length % AudioFormat.BytesPerFrame != 0
            || pcm.Length > AudioFormat.MaxChunkFrames * AudioFormat.BytesPerFrame
        )
            throw new ArgumentException("Supply 1–4800 complete PCM16 mono frames (2–9600 bytes).", nameof(pcm));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        try
        {
            await _writer.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                // The opaque session header prevents delayed writes reaching a subsequent playback session.
                var payload = new byte[16 + pcm.Length];
                _identity.CopyTo(payload, 0);
                pcm.CopyTo(payload.AsMemory(16));
                while (true)
                {
                    await CheckLeaseAsync().ConfigureAwait(false);
                    var reply = await _data.SendAsync<byte[]>(payload, linked.Token).ConfigureAwait(false);
                    if (reply is not { Length: 1 })
                        throw new ChannelSerializationException("Invalid audio acknowledgement.");
                    switch (reply[0])
                    {
                        case 0:
                            return;
                        case 1: // Full: no bytes accepted.
                        case 2: // Paused: no bytes accepted.
                            await Task.Delay(10, linked.Token).ConfigureAwait(false);
                            break;
                        default:
                            throw new ChannelException(
                                "audio_rejected",
                                "Native audio rejected this session or PCM chunk.",
                                reply[0]
                            );
                    }
                }
            }
            finally
            {
                _writer.Release();
            }
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            throw new ObjectDisposedException(nameof(AudioStreamSession));
        }
    }

    public Task PauseAsync(CancellationToken cancellationToken = default) => CommandAsync("pause", cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default) => CommandAsync("resume", cancellationToken);

    private async Task CommandAsync(string command, CancellationToken cancellation)
    {
        ThrowIfDisposed();
        await CheckLeaseAsync().ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _closed.Token);
        await _control.InvokeAsync(command, Arguments, linked.Token).ConfigureAwait(false);
    }

    public async Task<AudioPlaybackStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await CheckLeaseAsync().ConfigureAwait(false);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token);
        var value = await _control.InvokeAsync<object>("status", Arguments, linked.Token).ConfigureAwait(false);
        if (
            value is not IDictionary<string, object?> map
            || !map.TryGetValue("state", out var stateValue)
            || stateValue is not string state
            || !map.TryGetValue("bufferedFrames", out var bufferedValue)
            || bufferedValue is not int buffered
            || !map.TryGetValue("capacityFrames", out var capacityValue)
            || capacityValue is not int capacity
            || !map.TryGetValue("playedFrames", out var playedValue)
            || playedValue is not long played
        )
            throw new ChannelSerializationException("Invalid native audio status.");
        return new(state, buffered, capacity, played);
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var status = await GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.BufferedFrames == 0)
                return;
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CheckLeaseAsync()
    {
        if (!_lease.IsCompleted)
            return;
        await _lease.ConfigureAwait(false);
        throw new ChannelException("audio_closed", "The native audio session has ended.");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            return new(_dispose ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _closed.Cancel();
        try
        {
            await _control
                .InvokeAsync("stop", Arguments, options: new() { Timeout = TimeSpan.FromSeconds(2) })
                .ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        { /* The containing channel service already closed the bridge. */
        }
        finally
        {
            _leaseCancellation.Cancel();
            try
            {
                await _lease.ConfigureAwait(false);
            }
            catch
            { /* Observed by writes/status; cleanup still must finish. */
            }
            _data.Dispose();
            _control.Dispose();
        }
    }
}
