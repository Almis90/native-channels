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
            "Run host lifecycle checks",
            async () =>
            {
                await VerifyAsync();
                _status.Text =
                    "Host and Windows native-control checks passed: dispatch, lifecycle, parenting, layout, focus and direct input messages. "
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
        var pair = NativeChannels.InMemoryTransport.CreatePair();
        await using var client = new NativeChannels.ChannelService(new NativeChannels.BinaryMessenger(pair.Managed));
        await using var server = new NativeChannels.ChannelService(new NativeChannels.BinaryMessenger(pair.Native));
        server.GetMethodChannel("echo").SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        Require(await client.GetMethodChannel("echo").InvokeAsync<string>("echo", "hello") == "hello", "Method echo");
        server.GetBasicMessageChannel("messages/echo").SetMessageHandler((message, _) => ValueTask.FromResult(message));
        Require(
            await client.GetBasicMessageChannel("messages/echo").SendAsync<string>("hello") == "hello",
            "Message echo"
        );
#if SAMPLE_DESKTOP
        if (OperatingSystem.IsWindows())
        {
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
