using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeChannels;
using Plugin.VolumeMonitor;
using ReferenceSamples;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "VolumeMonitor";
    private const string Limitation =
        "Initial levels and subscription lifecycle are automated; hardware volume changes require a physical device.";
    private VolumeDemo? _demo;
    private IVolumeMonitor Volume =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddVolumeMonitor());
            services.AddVolumeMonitor();
        })
            .Services.GetRequiredService<IVolumeMonitor>();
    private VolumeDemo Demo => _demo ??= new VolumeDemo(Volume, _stopping.Token);
    private readonly TextBlock _level = new() { Text = "—", FontSize = 64 };
    private readonly ProgressBar _bar = new() { Maximum = 1, Height = 12 };
    private readonly TextBlock _count = new() { Text = "No events received" };
    private readonly TextBlock _verification = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _history = new() { Spacing = 8 };
    private readonly Button _start = new() { Content = "Start listening", IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private int _eventCount;
    private bool _initialized;

    private void Build()
    {
        var controls = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock
                {
                    Text = "Your volume. Live from native code.",
                    FontSize = 28,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = VolumeDemo.Instructions, TextWrapping = TextWrapping.Wrap },
                _level,
                _bar,
                _status,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 12,
                    Children = { _start, _stop },
                },
                _count,
                new TextBlock { Text = "Recent events", FontSize = 20 },
                _history,
                _verification,
                new TextBlock
                {
                    Text = "Stop removes the native observer; backgrounding this app also stops listening.",
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        };
        Content = new ScrollViewer { Content = controls };
        _start.Click += async (_, _) => await AttemptVolumeAsync(() => Demo.StartAsync());
        _stop.Click += async (_, _) => await AttemptVolumeAsync(Demo.StopAsync);
        Demo.ValueChanged += value =>
        {
            _level.Text = $"{value:P0}";
            _bar.Value = value;
            _count.Text = $"{++_eventCount} events received · last update {DateTime.Now:T}";
            _history.Children.Insert(
                0,
                new TextBlock { Text = $"{DateTime.Now:HH:mm:ss}   {value:P0}", FontSize = 14 }
            );
            if (_history.Children.Count > 12)
                _history.Children.RemoveAt(12);
        };
        Demo.StateChanged += text =>
        {
            if (text.Length != 0)
                _status.Text = text;
            _start.IsEnabled = Demo.Ready && !Demo.Listening;
            _stop.IsEnabled = Demo.Listening;
        };
        Demo.Verified += report => _verification.Text = report;
        Loaded += async (_, _) =>
        {
            if (
                _initialized
                || !string.IsNullOrEmpty(
                    Environment.GetEnvironmentVariable("NATIVE_session!.Channels_UNO_SAMPLE_RESULT")
                )
            )
                return;
            _initialized = true;
            await AttemptVolumeAsync(Demo.InitializeAsync);
        };
#if UNO_BROWSER_SAMPLE
        {
            controls.Children.Add(
                new TextBlock
                {
                    Text =
                        "Browsers cannot observe OS output volume. This separate example observes a page-owned HTML audio element.",
                    TextWrapping = TextWrapping.Wrap,
                }
            );
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0.5,
                StepFrequency = 0.01,
            };
            slider.ValueChanged += async (_, _) =>
                await AttemptVolumeAsync(async () =>
                {
                    using var channel = _session!.Channels.GetMethodChannel("example.browser.volume.control");
                    await channel.InvokeAsync("setVolume", slider.Value, _stopping.Token);
                });
            controls.Children.Add(slider);
            var pageStart = new Button { Content = "Start page-volume listening" };
            pageStart.Click += async (_, _) => await AttemptVolumeAsync(() => Demo.StartAsync(PageValues));
            controls.Children.Add(pageStart);
        }
#endif
    }

#if UNO_BROWSER_SAMPLE
    private async IAsyncEnumerable<double> PageValues(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token
    )
    {
        using var channel = _session!.Channels.GetEventChannel("example.browser.volume");
        await foreach (var value in channel.ListenAsync<double>(cancellationToken: token))
            yield return value;
    }
#endif

    private async Task AttemptVolumeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
    }

    private async Task VerifyAsync()
    {
#if UNO_BROWSER_SAMPLE
        _ = Demo;
        Console.WriteLine(await BrowserVolumeVerification.RunAsync(_session!.Channels, _stopping.Token));
#else
        Console.WriteLine(await Demo.VerifyAsync());
#endif
    }

    public async Task StopAsync()
    {
        if (_demo is null)
            return;
        if (_stopping.IsCancellationRequested)
            await _demo.DisposeAsync();
        else
            await _demo.SuspendAsync();
    }
}
