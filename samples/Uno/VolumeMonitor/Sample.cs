using NativeChannels;
using Plugin.VolumeMonitor;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "VolumeMonitor";
    private const string Limitation =
        "Windows, macOS ARM64 desktop, Linux x64 X11, iOS ARM64 simulator and Android x64 emulator subscriptions verified; hardware volume changes and other targets pending.";
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
        Button("Add system-volume listener", () => AddAsync());
        Button("Cancel last listener", CancelLastAsync);
        Button("Stop all listeners", StopAsync);
#if UNO_BROWSER_SAMPLE
        _ = Volume;
        _status.Text =
            "Browsers cannot observe OS output volume. The separate page-volume example observes an HTML audio element.";
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0.5,
        };
        slider.ValueChanged += async (_, _) =>
        {
            try
            {
                _ = Volume;
                using var channel = _session!.Channels.GetMethodChannel("example.browser.volume.control");
                await channel.InvokeAsync("setVolume", slider.Value, _stopping.Token);
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
            }
        };
        _panel.Children.Add(slider);
        Button("Add page-volume listener", () => AddAsync(true));
#endif
    }

    private Task AddAsync(bool page = false)
    {
        if (_stopping.IsCancellationRequested)
            return Task.CompletedTask;
        var volume = Volume;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        var run = WatchAsync(volume, ++_next, stop.Token, page);
        _listeners.Add((stop, run));
        return Task.CompletedTask;
    }

    private async Task WatchAsync(IVolumeMonitor volume, int id, CancellationToken token, bool page)
    {
        try
        {
            await using var events = page ? _session!.Channels.GetEventChannel("example.browser.volume") : null;
            var values = events is null
                ? volume.ListenAsync(token)
                : events.ListenAsync<double>(cancellationToken: token);
            await foreach (var value in values)
                _status.Text = $"{(page ? "Page" : "Output")} volume: {value:P0} · listener {id} · {DateTime.Now:T}";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
    }

    private async Task VerifyAsync()
    {
#if UNO_BROWSER_SAMPLE
        _ = Volume;
        var result = await ReferenceSamples.BrowserVolumeVerification.RunAsync(_session!.Channels, _stopping.Token);
#else
        var result = await global::VolumeMonitor.Sample.VolumeVerification.RunAsync(Volume, _stopping.Token);
#endif
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
