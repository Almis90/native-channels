using NativeChannels;
using Plugin.VolumeMonitor;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "VolumeMonitor";
    private const string Limitation = "Windows desktop verified; other platforms pending.";
    private CancellationTokenSource? _watch;
    private Task _watchTask = Task.CompletedTask;
    private IVolumeMonitor Volume =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddVolumeMonitor());
            services.AddVolumeMonitor();
        })
            .Services.GetRequiredService<IVolumeMonitor>();

    private void Build()
    {
        _status.Text = "Observe the system output volume. This sample does not change it.";
        Button(
            "Start",
            async () =>
            {
                await StopAsync();
                var volume = Volume;
                _watch = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                _watchTask = WatchAsync(volume, _watch.Token);
            }
        );
        Button("Stop", StopAsync);
    }

    private async Task WatchAsync(IVolumeMonitor volume, CancellationToken token)
    {
        try
        {
            await foreach (var value in volume.ListenAsync(token))
                _status.Text = $"Output volume: {value:P0}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
    }

    private async Task VerifyAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var stream = Volume.ListenAsync(timeout.Token).GetAsyncEnumerator();
        Require(await stream.MoveNextAsync() && stream.Current is >= 0 and <= 1, "Initial native volume");
    }

    public async Task StopAsync()
    {
        var watch = _watch;
        _watch = null;
        if (watch is null)
            return;
        watch.Cancel();
        await _watchTask;
        watch.Dispose();
        _status.Text = "Stopped.";
    }
}
