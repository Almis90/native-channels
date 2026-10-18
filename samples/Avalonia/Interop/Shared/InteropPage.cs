using Avalonia;
using Avalonia.Controls;
using NativeChannels;
using NativeChannels.Sample;

namespace ReferenceSamples;

internal sealed class InteropPage : SamplePage
{
    private readonly ViewVerification _views = new();
    private Task _run = Task.CompletedTask;
    private bool _finished;
    private readonly Button _runButton;
    private readonly Button _recreateButton;

    public InteropPage(AvaloniaChannelSession owner)
        : base(owner)
    {
        Status.Text = "Ready. Run the interoperability checks explicitly when testing this platform.";
        var clicks = new TextBlock
        {
            Text = "Tap either native button to call C#.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        };
        _views.Clicked += text => clicks.Text = text;
        _runButton = Button("Run checks", RunAsync);
        _recreateButton = Button("Recreate first native view", () => _views.ReconnectFirstAsync());
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "NativeChannels interoperability", FontSize = 26 },
                Status,
                _runButton,
                _views.Panel,
                clicks,
                _recreateButton,
            },
        };
    }

    private Task RunAsync()
    {
        if (IsClosed || !_run.IsCompleted || _finished)
            return Task.CompletedTask;
        return _run = CheckAsync();
    }

    private async Task CheckAsync()
    {
        Status.Text = "Checking native methods, messages, streams and views…";
        _runButton.IsEnabled = _recreateButton.IsEnabled = false;
        string report;
        try
        {
            // Manual taps before the suite must not change its initial-state assertions.
            await _views.ResetAsync();
            report = "PASS " + await new NativeVerification(Owner.Channels).RunAsync() + "; " + await _views.RunAsync();
            _finished = true;
        }
        catch (Exception error)
        {
            report = "FAIL " + error;
        }
        finally
        {
            _runButton.IsEnabled = !_finished && !IsClosed;
            _recreateButton.IsEnabled = !IsClosed;
        }
        Status.Text = report;
        Console.WriteLine("NATIVECHANNELS_AVALONIA_INTEROP " + report);
        if (!OperatingSystem.IsBrowser())
        {
            var path = Environment.GetEnvironmentVariable("NATIVECHANNELS_SAMPLE_RESULT");
            if (!string.IsNullOrWhiteSpace(path))
                File.WriteAllText(path, report);
        }
    }

    public override async Task SuspendAsync()
    {
        if (!IsClosed)
            return;
        await _views.DisconnectAsync();
        await Owner.DisposeAsync(); // Cancels running checks before awaiting their completion.
        await _run;
    }
}
