namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "Interop";
#if IOS || ANDROID || UNO_LINUX_SAMPLE || UNO_BROWSER_SAMPLE || UNO_MACOS_SAMPLE
    private const string Limitation =
        "Native methods and views use the shared MAUI/Avalonia checks; physical-device input remains unverified.";
#else
    private const string Limitation =
        "Windows native parenting, layout, focus and direct input-message checks; physical input and visual fidelity remain unverified. Other platforms pending.";
#endif
    private bool _ran;
#if SAMPLE_DESKTOP && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE
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
                    "Shared native method, message, stream and view checks passed, including 32 reconnects. "
                    + Limitation;
            }
        );
#if SAMPLE_DESKTOP && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE
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
#if !IOS && !ANDROID && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE && !UNO_BROWSER_SAMPLE
        await UnoFeasibility.HostProbe.RunAsync(_window, _surface);
#endif
#if SAMPLE_DESKTOP || IOS || ANDROID || UNO_BROWSER_SAMPLE || UNO_MACOS_SAMPLE
        if (
            OperatingSystem.IsWindows()
            || OperatingSystem.IsIOS()
            || OperatingSystem.IsAndroid()
            || OperatingSystem.IsMacOS()
            || OperatingSystem.IsLinux()
            || OperatingSystem.IsBrowser()
        )
        {
#if UNO_BROWSER_SAMPLE
            await ChannelExplorer.UnoBrowserButtonView.InitializeAsync();
#endif
            ChannelExplorer.SampleThread.Dispatcher = new NativeChannels.UnoChannelDispatcher(DispatcherQueue);
            var session = Session(services =>
                services.AddNativeChannels(options =>
                {
                    options.Modules.Add(ChannelExplorer.SamplePlatform.CreateModule());
                    options.Json.TypeInfoResolver = ChannelExplorer.SampleJsonContext.Default;
                })
            );
            Console.WriteLine(await new ChannelExplorer.NativeVerification(session.Channels).RunAsync());
            var parityViews = new ChannelExplorer.ViewVerification();
            _surface.Content = parityViews.Panel;
            try
            {
                _surface.UpdateLayout();
                Console.WriteLine(await parityViews.RunAsync());
            }
            finally
            {
                await parityViews.DisconnectAsync();
                _surface.Content = null;
            }
#if SAMPLE_DESKTOP && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE
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
#endif
        }
#endif
    }

    public async Task StopAsync()
    {
#if SAMPLE_DESKTOP && !UNO_LINUX_SAMPLE && !UNO_MACOS_SAMPLE
        if (_buttons is not null)
            await _buttons.DisposeAsync();
        _buttons = null;
#endif
        _surface.Content = null;
        await Task.CompletedTask;
    }
}
