namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Interop";
    private const string Limitation =
        "Windows native parenting, layout, focus and direct input-message checks; physical input and visual fidelity remain unverified. Other platforms pending.";
    private bool _ran;
#if SAMPLE_DESKTOP
    private NativeButtons? _buttons;
#endif

    private void Build()
    {
        _status.Text = Limitation;
        Button(
            "Run interoperability checks",
            async () =>
            {
                await VerifyAsync();
                _status.Text =
                    "Shared native method, message, stream and view checks passed, including 32 reconnects. Host and Windows input regressions passed. "
                    + Limitation;
            }
        );
#if SAMPLE_DESKTOP
        Button(
            "Show two native Windows controls",
            async () =>
            {
                if (!OperatingSystem.IsWindows())
                    throw new PlatformNotSupportedException("The native button adapter is Windows-only.");
                await StopAsync();
                _buttons = new NativeButtons();
                _surface.Content = _buttons.Panel;
                await Task.Delay(500);
                _status.Text =
                    _buttons.AttachmentStatus
                    + " Try clicking each button and resizing the window. Visual/input acceptance requires manual verification.";
            }
        );
        Button(
            "Reconnect first native view",
            () =>
            {
                _buttons?.Reconnect();
                return Task.CompletedTask;
            }
        );
        Button("Remove native views", StopAsync);
#endif
    }

    private async Task VerifyAsync()
    {
        if (_ran)
            throw new InvalidOperationException("Restart the app to repeat the application registration checks.");
        _ran = true;
        await StopAsync();
        await UnoFeasibility.HostProbe.RunAsync(_window, _surface);
#if SAMPLE_DESKTOP
        if (OperatingSystem.IsWindows())
        {
            ChannelExplorer.SampleThread.Dispatcher = new NativeChannels.UnoChannelDispatcher(DispatcherQueue);
            var session = Session(services =>
                services.AddNativeChannels(options =>
                {
                    options.Modules.Add(ChannelExplorer.SamplePlatform.CreateModule());
                    options.Json.TypeInfoResolver = ChannelExplorer.SampleJsonContext.Default;
                })
            );
            await new ChannelExplorer.NativeVerification(session.Channels).RunAsync();
            var parityViews = new ChannelExplorer.ViewVerification();
            _surface.Content = parityViews.Panel;
            try
            {
                _surface.UpdateLayout();
                await parityViews.RunAsync();
            }
            finally
            {
                await parityViews.DisconnectAsync();
                _surface.Content = null;
            }
            _buttons = new NativeButtons();
            _surface.Content = _buttons.Panel;
            try
            {
                await _buttons.VerifyAsync();
            }
            finally
            {
                await StopAsync();
            }
        }
#endif
    }

    public async Task StopAsync()
    {
#if SAMPLE_DESKTOP
        if (_buttons is not null)
            await _buttons.DisposeAsync();
        _buttons = null;
#endif
        _surface.Content = null;
        await Task.CompletedTask;
    }
}
