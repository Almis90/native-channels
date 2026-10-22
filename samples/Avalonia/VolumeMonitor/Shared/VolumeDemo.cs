using Plugin.VolumeMonitor;
using VolumeMonitor.Sample;

namespace ReferenceSamples;

// Shared sample behavior; the plugin deliberately does not own the app's audio session.
internal sealed class VolumeDemo(IVolumeMonitor volume, CancellationToken stopping) : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = CancellationTokenSource.CreateLinkedTokenSource(stopping);
    private AudioSessionScope? _audioSession;
    private CancellationTokenSource? _listener;
    private Task _run = Task.CompletedTask;
    private Task? _initialize;
    private bool _foreground = true;
    private bool _disposed;
#if IOS
    private Foundation.NSObject? _resume;
#endif
    public bool Ready { get; private set; }
    public bool Listening { get; private set; }
    public event Action<double>? ValueChanged;
    public event Action<string>? StateChanged;
    public event Action<string>? Verified;

    public Task InitializeAsync() => _initialize ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
#if IOS
        _resume = Foundation.NSNotificationCenter.DefaultCenter.AddObserver(
            UIKit.UIApplication.DidBecomeActiveNotification,
            _ => Resume()
        );
#endif
        try
        {
            if (!OperatingSystem.IsBrowser())
            {
                StateChanged?.Invoke("Checking native subscriptions…");
                var report = await VerifyAsync();
                Verified?.Invoke(report);
            }
            Ready = true;
            StateChanged?.Invoke("Ready");
            if (_foreground && !_lifetime.IsCancellationRequested && !OperatingSystem.IsBrowser())
                await StartAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            StateChanged?.Invoke(error.Message);
        }
    }

    public async Task<string> VerifyAsync()
    {
        EnsureAudioSession();
        return await VolumeVerification.RunAsync(volume, _lifetime.Token);
    }

    private void EnsureAudioSession()
    {
        if (!_disposed && !OperatingSystem.IsBrowser())
            _audioSession ??= new AudioSessionScope();
    }

    private void Resume()
    {
        if (_disposed)
            return;
        _foreground = true;
        try
        {
            EnsureAudioSession();
        }
        catch (Exception error)
        {
            StateChanged?.Invoke(error.Message);
        }
        // As in MAUI, returning to the app does not restart its stopped listener.
    }

    public Task StartAsync(Func<CancellationToken, IAsyncEnumerable<double>>? source = null)
    {
        if (_disposed || !Ready || !_run.IsCompleted)
            return Task.CompletedTask;
        _foreground = true;
        _listener?.Dispose();
        _listener = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _run = ListenAsync(source, _listener.Token);
        return Task.CompletedTask;
    }

    private async Task ListenAsync(Func<CancellationToken, IAsyncEnumerable<double>>? source, CancellationToken token)
    {
        try
        {
            EnsureAudioSession();
            Listening = true;
            StateChanged?.Invoke("Listening");
            await foreach (var value in source is null ? volume.ListenAsync(token) : source(token))
                ValueChanged?.Invoke(value);
            StateChanged?.Invoke("Stream completed");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            StateChanged?.Invoke("Stopped");
        }
        catch (Exception error)
        {
            StateChanged?.Invoke(error.Message);
        }
        finally
        {
            Listening = false;
            StateChanged?.Invoke("");
        }
    }

    public async Task StopAsync()
    {
        _listener?.Cancel();
        await _run;
        StateChanged?.Invoke("Stopped");
    }

    public async Task SuspendAsync()
    {
        _foreground = false;
        var stoppingListener = StopAsync();
        var session = _audioSession;
        _audioSession = null;
        try
        {
            session?.Dispose();
        }
        finally
        {
            await stoppingListener;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _lifetime.Cancel();
#if IOS
        _resume?.Dispose();
        _resume = null;
#endif
        try
        {
            if (_initialize is not null)
                await _initialize;
            await SuspendAsync();
        }
        finally
        {
            _listener?.Dispose();
            _lifetime.Dispose();
        }
    }

    public static string Instructions =>
        OperatingSystem.IsIOS()
            ? "Apple output volume · KVO observes AVAudioSession. Use a physical iPhone to verify changes; the simulator may report only its initial level."
            : "Change media volume with the device controls. This shows level changes, not every button press.";
}
