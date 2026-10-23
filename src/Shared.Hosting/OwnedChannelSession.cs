using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>An independent channel session and service provider, owned by a UI host.</summary>
internal sealed class OwnedChannelSession : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IHostDispatcher _dispatcher;
    private readonly Action<OwnedChannelSession> _released;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private Task? _disposal;
    private IChannelService? _channels;

    internal OwnedChannelSession(
        ServiceProvider provider,
        IHostDispatcher dispatcher,
        Action<OwnedChannelSession> released
    )
    {
        _provider = provider;
        _dispatcher = dispatcher;
        _released = released;
        Stopping = _stopping.Token;
    }

    /// <summary>Cancelled before owned services are disposed. Link view work to this token.</summary>
    public CancellationToken Stopping { get; }

    /// <summary>Services owned by this session. Singleton registrations are isolated between sessions.</summary>
    public IServiceProvider Services
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposal is not null, this);
                if (_channels is null)
                {
                    _dispatcher.VerifyAccess();
                    _channels = _provider.GetRequiredService<IChannelService>();
                }
                return _provider;
            }
        }
    }

    public IChannelService Channels
    {
        get
        {
            _ = Services;
            return _channels!;
        }
    }

    /// <summary>Shares one completion across repeated calls; cancellation and disposal begin on the UI thread.</summary>
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (_disposal is not null)
                return new(_disposal);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposal = completion.Task;
        }
        _ = CompleteDisposalAsync(completion);
        return new(completion.Task);
    }

    private async Task CompleteDisposalAsync(TaskCompletionSource completion)
    {
        try
        {
            if (_dispatcher.HasThreadAccess)
                await DisposeCoreAsync();
            else
                await _dispatcher.InvokeAsync(DisposeCoreAsync);
            completion.SetResult();
        }
        catch (Exception error)
        {
            completion.SetException(error);
        }
    }

    private async Task DisposeCoreAsync()
    {
        var errors = new List<Exception>();
        try
        {
            try
            {
                _stopping.Cancel();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            // Preserve DI's reverse creation order so plugins can send their final stop/unlisten.
            try
            {
                await _provider.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
            try
            {
                // A throwing application service can interrupt DI disposal before it reaches channels.
                if (_channels is not null)
                    await _channels.DisposeAsync();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }
        }
        finally
        {
            _stopping.Dispose();
            _released(this);
        }
        if (errors.Count > 0)
            throw new AggregateException("NativeChannels session cleanup failed.", errors);
    }
}
