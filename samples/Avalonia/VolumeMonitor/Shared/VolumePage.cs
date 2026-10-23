using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.VolumeMonitor;

namespace ReferenceSamples;

internal sealed class VolumePage : SamplePage
{
    private readonly VolumeDemo _demo;
    private VolumeDemo Demo => _demo;
    private readonly TextBlock _level = new() { Text = "—", FontSize = 64 };
    private readonly ProgressBar _bar = new() { Maximum = 1, Height = 12 };
    private readonly TextBlock _count = new() { Text = "No events received" };
    private readonly TextBlock _verification = new() { FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel _history = new() { Spacing = 8 };
    private readonly Button _start = new() { Content = "Start listening", IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private int _eventCount;
    private bool _initialized;

    public VolumePage(AvaloniaChannelSession owner)
        : base(owner)
    {
        _demo = new VolumeDemo(owner.Services.GetRequiredService<IVolumeMonitor>(), owner.Stopping);
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
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new TextBlock { Text = VolumeDemo.Instructions, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                _level,
                _bar,
                Status,
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
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
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
            },
        };
        Content = new ScrollViewer { Content = controls };
        _start.Click += async (_, _) => await Attempt(() => Demo.StartAsync());
        _stop.Click += async (_, _) => await Attempt(Demo.StopAsync);
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
                Status.Text = text;
            _start.IsEnabled = Demo.Ready && !Demo.Listening;
            _stop.IsEnabled = Demo.Listening;
        };
        Demo.Verified += report => _verification.Text = report;
        Loaded += async (_, _) =>
        {
            if (_initialized || SampleApplication.VerifyOnLoad)
                return;
            _initialized = true;
            await Attempt(Demo.InitializeAsync);
        };
        if (OperatingSystem.IsBrowser())
        {
            controls.Children.Add(
                new TextBlock
                {
                    Text =
                        "Browsers cannot observe OS output volume. This separate example observes a page-owned HTML audio element.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                }
            );
            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 1,
                Value = 0.5,
            };
            slider.PropertyChanged += async (_, args) =>
            {
                if (args.Property == Slider.ValueProperty)
                    await Attempt(async () =>
                    {
                        using var channel = Owner.Channels.GetMethodChannel("example.browser.volume.control");
                        await channel.InvokeAsync("setVolume", slider.Value, Owner.Stopping);
                    });
            };
            controls.Children.Add(slider);
            var pageStart = new Button { Content = "Start page-volume listening" };
            pageStart.Click += async (_, _) => await Attempt(() => Demo.StartAsync(PageValues));
            controls.Children.Add(pageStart);
        }
    }

    private async IAsyncEnumerable<double> PageValues(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token
    )
    {
        using var channel = Owner.Channels.GetEventChannel("example.browser.volume");
        await foreach (var value in channel.ListenAsync<double>(cancellationToken: token))
            yield return value;
    }

    public override Task<string> VerifyAsync() =>
        OperatingSystem.IsBrowser()
            ? BrowserVolumeVerification.RunAsync(Owner.Channels, Owner.Stopping)
            : Demo.VerifyAsync();

    public override async Task SuspendAsync()
    {
        if (IsClosed)
            await Demo.DisposeAsync();
        else
            await Demo.SuspendAsync();
    }
}
