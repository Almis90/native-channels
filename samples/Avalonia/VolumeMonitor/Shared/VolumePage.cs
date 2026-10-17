using Avalonia;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Maui.VolumeMonitor;

namespace ReferenceSamples;

internal sealed class VolumePage : SamplePage
{
    private readonly IVolumeMonitor _volume;
    private readonly TextBlock _level = new() { Text = "No subscription", FontSize = 32 };
    private readonly ProgressBar _bar = new() { Maximum = 1 };
    private readonly List<(CancellationTokenSource Stop, Task Run)> _listeners = [];
    private int _next;

    public VolumePage(AvaloniaChannelSession owner)
        : base(owner)
    {
        _volume = owner.Services.GetRequiredService<IVolumeMonitor>();
        var controls = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = "Output volume events", FontSize = 28 },
                new TextBlock
                {
                    Text =
                        "Listen for the initial value and changes. Use your device's volume controls. Each listener has its own lifetime.",
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                _level,
                _bar,
                Status,
                Button("Add system-volume listener", () => AddAsync(false)),
                Button("Cancel last listener", CancelLastAsync),
                Button("Stop all listeners", SuspendAsync),
            },
        };
        if (OperatingSystem.IsBrowser())
        {
            controls.Children.Add(
                new TextBlock
                {
                    Text =
                        "Browsers cannot observe OS output volume. The separate example below observes a page-owned HTML audio element.",
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
            controls.Children.Add(Button("Add page-volume listener", () => AddAsync(true)));
        }
        Content = new ScrollViewer { Content = controls };
    }

    private Task AddAsync(bool page)
    {
        if (IsClosed)
            return Task.CompletedTask;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(Owner.Stopping);
        var id = ++_next;
        var run = Attempt(() => ListenAsync(id, page, stop.Token));
        _listeners.Add((stop, run));
        return Task.CompletedTask;
    }

    private async Task ListenAsync(int id, bool page, CancellationToken token)
    {
        if (page)
        {
            await using var events = Owner.Channels.GetEventChannel("example.browser.volume");
            await foreach (var value in events.ListenAsync<double>(cancellationToken: token))
                Update(id, value, "Page");
        }
        else
            await foreach (var value in _volume.ListenAsync(token))
                Update(id, value, "System");
    }

    private void Update(int id, double value, string source)
    {
        _level.Text = $"{source}: {value:P0}";
        _bar.Value = value;
        Status.Text = $"Listener {id} received an event at {DateTime.Now:T}";
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
        Status.Text = $"{_listeners.Count} listener(s) remain";
    }

    public override async Task SuspendAsync()
    {
        while (_listeners.Count != 0)
            await CancelLastAsync();
    }
}
