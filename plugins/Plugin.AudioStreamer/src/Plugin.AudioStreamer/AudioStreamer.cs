using NativeChannels;

namespace Plugin.AudioStreamer;

public interface IAudioStreamer : IAsyncDisposable
{
    Task<AudioStreamSession> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Owns one foreground playback session at a time. Dispose the session to release native audio resources.</summary>
public sealed class AudioStreamer(IChannelService channels) : IAudioStreamer
{
    private readonly SemaphoreSlim _gate = new(1);
    private AudioStreamSession? _session;
    private bool _disposed;

    public async Task<AudioStreamSession> OpenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_session is { IsDisposed: false })
                throw new InvalidOperationException("Dispose the current audio session before opening another.");
            if (_session is not null)
                await _session.DisposeAsync().ConfigureAwait(false);
            var session = new AudioStreamSession(channels);
            _session = session;
            try
            {
                await session.InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
                catch
                { /* Preserve the startup error. */
                }
                _session = null;
                throw;
            }
            return session;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_session is not null)
                await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
