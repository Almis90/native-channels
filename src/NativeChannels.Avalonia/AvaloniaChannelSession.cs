using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace NativeChannels;

/// <summary>An independent channel session and service provider, owned by an Avalonia host.</summary>
public sealed class AvaloniaChannelSession : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly Action<AvaloniaChannelSession> _released;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _gate = new();
    private Task? _disposal;
    private IChannelService? _channels;

    internal AvaloniaChannelSession(ServiceProvider provider, Action<AvaloniaChannelSession> released)
    {
        _provider = provider;
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
                    Dispatcher.UIThread.VerifyAccess();
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
            if (Dispatcher.UIThread.CheckAccess())
                await DisposeCoreAsync();
            else
                await Dispatcher.UIThread.InvokeAsync(DisposeCoreAsync);
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
