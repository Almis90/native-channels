using NativeChannels;
using Plugin.VolumeMonitor;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "VolumeMonitor";
    private const string Limitation =
        "Windows and macOS ARM64 desktop subscriptions verified; hardware volume changes and other platforms pending.";
    private readonly List<(CancellationTokenSource Stop, Task Run)> _listeners = [];
    private int _next;
    private IVolumeMonitor Volume =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddVolumeMonitor());
            services.AddVolumeMonitor();
        })
            .Services.GetRequiredService<IVolumeMonitor>();

    private void Build()
    {
        _status.Text =
            "Listen for the initial output volume and changes. Use your device's volume controls. Each listener has its own lifetime.";
        Button("Add system-volume listener", AddAsync);
        Button("Cancel last listener", CancelLastAsync);
        Button("Stop all listeners", StopAsync);
    }

    private Task AddAsync()
    {
        if (_stopping.IsCancellationRequested)
            return Task.CompletedTask;
        var volume = Volume;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        var run = WatchAsync(volume, ++_next, stop.Token);
        _listeners.Add((stop, run));
        return Task.CompletedTask;
    }

    private async Task WatchAsync(IVolumeMonitor volume, int id, CancellationToken token)
    {
        try
        {
            await foreach (var value in volume.ListenAsync(token))
                _status.Text = $"Output volume: {value:P0} · listener {id} · {DateTime.Now:T}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
    }

    private async Task VerifyAsync()
    {
        var result = await global::VolumeMonitor.Sample.VolumeVerification.RunAsync(Volume, _stopping.Token);
        Console.WriteLine(result);
    }

    private async Task CancelLastAsync()
    {
        if (_listeners.Count == 0)
            return;
        var last = _listeners[^1];
        _listeners.RemoveAt(_listeners.Count - 1);
        last.Stop.Cancel();
        try
        {
            await last.Run;
        }
        finally
        {
            last.Stop.Dispose();
        }
        _status.Text = $"{_listeners.Count} listener(s) remain";
    }

    public async Task StopAsync()
    {
        while (_listeners.Count != 0)
            await CancelLastAsync();
        _status.Text = "Stopped.";
    }
}
