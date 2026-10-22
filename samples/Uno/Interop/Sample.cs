using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Interop";
    private const string Limitation =
        "Native methods and views use the shared MAUI/Avalonia checks; physical-device input remains unverified.";
    private ChannelExplorer.ViewVerification? _views;
    private Task? _initialize;
    private bool _ran;
    private readonly Button _runButton = new() { Content = "Run checks" };
    private readonly Button _recreateButton = new() { Content = "Recreate first native view" };
    private readonly TextBlock _clicks = new()
    {
        Text = "Tap either native button to call C#.",
        TextWrapping = TextWrapping.Wrap,
    };

    private void Build()
    {
        ((TextBlock)_panel.Children[0]).Text = "NativeChannels interoperability";
        _status.Text = "Ready. Run the interoperability checks explicitly when testing this platform.";
        _panel.Children.Remove(_surface);
        _panel.Children.Add(_runButton);
        _panel.Children.Add(_surface);
        _panel.Children.Add(_clicks);
        _panel.Children.Add(_recreateButton);
        _runButton.Click += async (_, _) => await ActAsync(VerifyAsync);
        _recreateButton.Click += async (_, _) =>
            await ActAsync(async () =>
            {
                await InitializeViewsAsync();
                await _views!.ReconnectFirstAsync();
            });
        Loaded += async (_, _) => await ActAsync(InitializeViewsAsync);
    }

    private async Task ActAsync(Func<Task> action)
    {
        _runButton.IsEnabled = _recreateButton.IsEnabled = false;
        await _actions.WaitAsync();
        try
        {
            if (!_stopping.IsCancellationRequested)
                await action();
        }
        catch (Exception error)
        {
            _status.Text = "FAIL " + error;
        }
        finally
        {
            _actions.Release();
            _runButton.IsEnabled = !_ran && !_stopping.IsCancellationRequested;
            _recreateButton.IsEnabled = !_stopping.IsCancellationRequested;
        }
    }

    private Task InitializeViewsAsync() => _initialize ??= InitializeViewsCoreAsync();

    private async Task InitializeViewsCoreAsync()
    {
#if UNO_BROWSER_SAMPLE
        await ChannelExplorer.UnoBrowserButtonView.InitializeAsync();
#endif
        ChannelExplorer.SampleThread.Dispatcher = new NativeChannels.UnoChannelDispatcher(DispatcherQueue);
        _views = new ChannelExplorer.ViewVerification();
        _views.Clicked += text => _clicks.Text = text;
        _surface.Content = _views!.Panel;
        await Task.CompletedTask;
    }

    private async Task VerifyAsync()
    {
        if (_ran)
            return;
        _status.Text = "Checking native methods, messages, streams and views…";
        await InitializeViewsAsync();
#if SAMPLE_DESKTOP && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE
        // Preserve the existing Windows host diagnostics, then restore the interactive views.
        try
        {
            await UnoFeasibility.HostProbe.RunAsync(_window, _surface);
            await using var buttons = new NativeButtons();
            _surface.Content = buttons.Panel;
            await buttons.VerifyAsync();
        }
        finally
        {
            _surface.Content = _views!.Panel;
        }
#endif
        var session = Session(services =>
            services.AddNativeChannels(options =>
            {
                options.Modules.Add(ChannelExplorer.SamplePlatform.CreateModule());
                options.Json.TypeInfoResolver = ChannelExplorer.SampleJsonContext.Default;
            })
        );
        // Reset manual taps before running the same initial-state assertions as Avalonia.
        await _views!.ResetAsync();
        var report = "PASS " + await new ChannelExplorer.NativeVerification(session.Channels).RunAsync();
        _surface.UpdateLayout();
        report += "; " + await _views.RunAsync();

        _ran = true;
        _status.Text = report;
        Console.WriteLine("NATIVECHANNELS_UNO_INTEROP " + report);
    }

    public async Task StopAsync()
    {
        // Keep the interactive views when the app merely enters the background.
        // The framework handles unload/reload; explicit teardown belongs to closing.
        if (!_stopping.IsCancellationRequested)
            return;
        if (_initialize is not null)
            await _initialize;
        if (_views is not null)
            await _views.DisconnectAsync();
        _surface.Content = null;
    }
}
