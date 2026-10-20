using PdfControl = Plugin.Uno.PdfViewer.PdfViewer;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "PdfViewer";
    private const string Limitation =
        "Windows.Data.Pdf renders into a native HWND. Physical input/visual review and other platform adapters remain unverified.";
    private PdfControl? _pdf;
    private readonly TextBox _path = new()
    {
        Header = "Absolute PDF path",
        Text = Path.Combine(AppContext.BaseDirectory, "nativechannels-guide.pdf"),
    };

    private void Build()
    {
        _status.Text = Limitation;
        _panel.Children.Add(_path);
        Button(
            "Open PDF",
            async () =>
            {
                if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
                    throw new PlatformNotSupportedException("The bundled renderer requires Windows 10 1809 or later.");
                if (_pdf is null)
                {
                    _pdf = new PdfControl
                    {
                        Height = 420,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch,
                    };
                    _pdf.StateChanged += (_, state) =>
                        _status.Text = $"Page {state.Page}/{state.PageCount}, zoom {state.Zoom:0.0}×";
                    _surface.Content = _pdf;
                    await WaitForPdfAsync(_pdf);
                }
                await _pdf.OpenAsync(_path.Text);
            }
        );
        Button(
            "Previous page",
            async () =>
            {
                if (_pdf?.State is { } state)
                    await _pdf.GoToPageAsync(Math.Max(1, state.Page - 1));
            }
        );
        Button(
            "Next page",
            async () =>
            {
                if (_pdf?.State is { } state)
                    await _pdf.GoToPageAsync(Math.Min(state.PageCount, state.Page + 1));
            }
        );
        Button(
            "Zoom in",
            async () =>
            {
                if (_pdf?.State is { } state)
                    await _pdf.SetZoomAsync(Math.Min(4, state.Zoom + 0.5));
            }
        );
        Button(
            "Zoom out",
            async () =>
            {
                if (_pdf?.State is { } state)
                    await _pdf.SetZoomAsync(Math.Max(1, state.Zoom - 0.5));
            }
        );
        Button(
            "Reconnect and reopen",
            async () =>
            {
                if (_pdf is null)
                    return;
                _pdf.Reconnect();
                await WaitForPdfAsync(_pdf);
                await _pdf.OpenAsync(_path.Text);
            }
        );
        Button("Close document", StopAsync);
        Button(
            "Check PDF control protocol",
            async () =>
            {
                await StopAsync();
                await UnoFeasibility.PdfProtocolProbe.RunAsync(_surface);
                _status.Text = "Protocol passed: page, zoom, callbacks, identity, reconnect and cleanup. " + Limitation;
            }
        );
    }

    private static async Task WaitForPdfAsync(PdfControl pdf)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!pdf.IsConnected)
            await Task.Delay(25, timeout.Token);
    }

    private async Task VerifyAsync()
    {
#if SAMPLE_DESKTOP
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            await NativePdfProbe.RunAsync(_surface, _path.Text);
            return;
        }
#endif
        throw new PlatformNotSupportedException("Native PDF acceptance currently requires Windows.");
    }

    public async Task StopAsync()
    {
        if (_pdf is not null)
            await _pdf.DisposeAsync();
        _pdf = null;
        _surface.Content = null;
    }
}
