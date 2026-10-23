using PdfControl = Plugin.Uno.PdfViewer.PdfViewer;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "PdfViewer";
#if UNO_BROWSER_SAMPLE
    private const string Limitation =
        "Browser PDF.js views use the existing Avalonia verifier; physical input and visual acceptance remain unverified.";
#elif IOS || ANDROID || UNO_LINUX_SAMPLE || UNO_MACOS_SAMPLE
    private const string Limitation =
        "Native PDF views; automated checks use the existing MAUI PDF verifier. Physical-device input remains unverified.";
#else
    private const string Limitation =
        "Windows.Data.Pdf renders into a native HWND. Physical input/visual review and other platform adapters remain unverified.";
#endif
    private PdfControl? _pdf;
#if UNO_BROWSER_SAMPLE
    private byte[]? _browserPdfBytes;
#endif
    private readonly TextBox _path = new()
    {
        Header = "Absolute PDF path",
        Text = Path.Combine(AppContext.BaseDirectory, "nativechannels-guide.pdf"),
    };

    private void Build()
    {
#if UNO_LINUX_SAMPLE && PDF_GTK
        Plugin.Uno.PdfViewer.PdfLinux.Backend = Plugin.Uno.PdfViewer.LinuxPdfBackend.Gtk;
#endif
#if IOS || ANDROID || UNO_LINUX_SAMPLE || UNO_MACOS_SAMPLE
        PdfVerificationThread.Dispatcher = DispatcherQueue;
        using (
            var input =
                typeof(SamplePage).Assembly.GetManifestResourceStream("Verification.pdf")
                ?? throw new InvalidOperationException("Missing verification PDF.")
        )
        {
            _path.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nativechannels-guide.pdf"
            );
            using var output = File.Create(_path.Text);
            input.CopyTo(output);
        }
#endif
        _status.Text = "Checking packaged native viewer…";
        _panel.Children.Remove(_surface);
        _panel.Children.Remove(_status);
        var layout = new Grid { Margin = new Thickness(16, 12, 16, 12), RowSpacing = 10 };
        foreach (
            var height in new[]
            {
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star),
                GridLength.Auto,
                GridLength.Auto,
                GridLength.Auto,
            }
        )
            layout.RowDefinitions.Add(new RowDefinition { Height = height });
        _position.Text = "Opening document…";
        _position.HorizontalAlignment = HorizontalAlignment.Center;
        layout.Children.Add(_position);
        Grid.SetRow(_surface, 1);
        layout.Children.Add(_surface);
        _navigation.Children.Add(
            PdfAction("Previous", async () => await _pdf!.GoToPageAsync(Math.Max(1, (_pdf.State?.Page ?? 1) - 1)))
        );
        _navigation.Children.Add(
            PdfAction(
                "Next",
                async () => await _pdf!.GoToPageAsync(Math.Min(_pdf.State?.PageCount ?? 1, (_pdf.State?.Page ?? 0) + 1))
            )
        );
        _zoom.Children.Add(
            PdfAction("−", async () => await _pdf!.SetZoomAsync(Math.Max(1, (_pdf.State?.Zoom ?? 1) - 0.5)))
        );
        _zoom.Children.Add(PdfAction("Fit page", async () => await _pdf!.SetZoomAsync(1)));
        _zoom.Children.Add(
            PdfAction("+", async () => await _pdf!.SetZoomAsync(Math.Min(4, (_pdf.State?.Zoom ?? 1) + 0.5)))
        );
        _zoom.Children.Add(
            PdfAction(
                "Reopen",
                async () =>
                {
                    _pdf!.Reconnect();
                    await WaitForPdfAsync(_pdf);
                    await OpenGuideAsync();
                }
            )
        );
        Grid.SetRow(_navigation, 2);
        Grid.SetRow(_zoom, 3);
        layout.Children.Add(_navigation);
        layout.Children.Add(_zoom);
        var details = new StackPanel { Spacing = 6 };
        details.Children.Add(
            new TextBlock
            {
                Text = OperatingSystem.IsWindows()
                    ? "Use Previous and Next to turn pages. Zoom with +/− or pinch, then scroll to pan. Reopen creates a fresh native view."
                    : "Pinch to zoom, drag to pan. At fit, swipe to turn pages. Reopen releases the document and creates a fresh native view.",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
            }
        );
        _status.FontSize = 12;
        details.Children.Add(_status);
        Grid.SetRow(details, 4);
        layout.Children.Add(details);
        Content = layout;
        SetPdfWorking(true);
        Loaded += async (_, _) =>
        {
            if (
                _pdfStarted
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NATIVECHANNELS_UNO_SAMPLE_RESULT"))
            )
                return;
            _pdfStarted = true;
            _pdfStartup = InitializePdfAsync();
            await _pdfStartup;
        };
    }

    private readonly TextBlock _position = new();
    private readonly StackPanel _navigation = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 8,
    };
    private readonly StackPanel _zoom = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 8,
    };
    private bool _pdfStarted;
    private Task _pdfStartup = Task.CompletedTask;

    private Button PdfAction(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) =>
        {
            SetPdfWorking(true);
            await _actions.WaitAsync();
            try
            {
                if (_stopping.IsCancellationRequested)
                    return;
                await action();
                _status.Text = "Page and zoom state received from native code.";
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
            }
            finally
            {
                _actions.Release();
                SetPdfWorking(_stopping.IsCancellationRequested);
            }
        };
        return button;
    }

    private void SetPdfWorking(bool working)
    {
        foreach (var button in _navigation.Children.Concat(_zoom.Children).OfType<Button>())
            button.IsEnabled = !working;
    }

    private async Task InitializePdfAsync()
    {
        try
        {
            await VerifyAsync();
            _stopping.Token.ThrowIfCancellationRequested();
            _pdf = new PdfControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            _pdf.StateChanged += (_, state) =>
                _position.Text = $"Page {state.Page} of {state.PageCount}  ·  {state.Zoom:P0}";
            _surface.Content = _pdf;
            await WaitForPdfAsync(_pdf);
            await OpenGuideAsync();
            _status.Text = "Native document, navigation, callbacks and cleanup verified.";
            SetPdfWorking(false);
        }
        catch (Exception error)
        {
            _status.Text = "FAIL " + error.Message;
        }
    }

    private async Task OpenGuideAsync()
    {
#if UNO_BROWSER_SAMPLE
        if (_browserPdfBytes is null)
        {
            using var input =
                typeof(SamplePage).Assembly.GetManifestResourceStream("Verification.pdf")
                ?? throw new InvalidOperationException("Missing verification PDF.");
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, _stopping.Token);
            _browserPdfBytes = buffer.ToArray();
        }
        await _pdf!.OpenBytesAsync(_browserPdfBytes, _stopping.Token);
#else
        await _pdf!.OpenAsync(_path.Text, _stopping.Token);
#endif
    }

    private static async Task WaitForPdfAsync(PdfControl pdf)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!pdf.IsConnected)
            await Task.Delay(25, timeout.Token);
    }

    private async Task VerifyAsync()
    {
#if UNO_BROWSER_SAMPLE
        await Plugin.Uno.PdfViewer.PdfBrowser.InitializeAsync();
        await using var first = new PdfControl { Height = 240 };
        await using var second = new PdfControl { Height = 240 };
        _surface.Content = new StackPanel { Children = { first, second } };
        try
        {
            Console.WriteLine(
                await ReferenceSamples.SharedPdfVerification.RunAsync([first, second], [], _stopping.Token)
            );
        }
        finally
        {
            await first.DisconnectAsync();
            await second.DisconnectAsync();
            _surface.Content = null;
        }
#else
#if IOS || ANDROID || UNO_LINUX_SAMPLE || UNO_MACOS_SAMPLE
        await using var first = new PdfControl { Height = 240 };
        await using var second = new PdfControl { Height = 240 };
        _surface.Content = new StackPanel { Children = { first, second } };
        try
        {
            _surface.UpdateLayout();
            await WaitForPdfAsync(first);
            await WaitForPdfAsync(second);
            Console.WriteLine(await global::PdfViewer.Sample.PdfVerification.RunAsync(first, second, _path.Text));
        }
        finally
        {
            await first.DisconnectAsync();
            await second.DisconnectAsync();
            _surface.Content = null;
        }
#else
#if SAMPLE_DESKTOP
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            await NativePdfProbe.RunAsync(_surface, _path.Text);
            return;
        }
#endif
        throw new PlatformNotSupportedException("Native PDF acceptance currently requires Windows.");
#endif
#endif
    }

    public async Task StopAsync()
    {
        // Preserve the open document when merely switching apps, as MAUI does.
        if (!_stopping.IsCancellationRequested)
            return;
        await _pdfStartup;
        if (_pdf is not null)
            await _pdf.DisposeAsync();
        _pdf = null;
        _surface.Content = null;
    }
}

#if IOS || ANDROID || UNO_LINUX_SAMPLE || UNO_MACOS_SAMPLE
internal static class PdfVerificationThread
{
    public static Microsoft.UI.Dispatching.DispatcherQueue Dispatcher { get; set; } = null!;
    public static bool IsMainThread => Dispatcher.HasThreadAccess;
}
#endif
