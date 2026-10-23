using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using NativeChannels;
using Plugin.Avalonia.PdfViewer;

namespace ReferenceSamples;

internal sealed class PdfPage : SamplePage
{
    private readonly PdfViewer _viewer = new();
    private readonly PdfViewer _probe = new()
    {
        Height = 1,
        Opacity = 0,
        IsHitTestVisible = false,
    };
    private readonly List<string> _temporary = [];
    private readonly CancellationTokenSource _operations = new();
    private readonly TextBlock _position = new()
    {
        Text = "Opening document…",
        HorizontalAlignment = HorizontalAlignment.Center,
    };
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
    private readonly Grid _layout;
    private bool _started;
    private bool _working = true;
    private Task _startup = Task.CompletedTask;
    private Task _action = Task.CompletedTask;

    public PdfPage(AvaloniaChannelSession owner)
        : base(owner)
    {
#if LINUX
#if PDF_GTK
        PdfLinux.Backend = LinuxPdfBackend.Gtk;
#else
        PdfLinux.Backend = LinuxPdfBackend.Qt;
#endif
#endif
        Status.Text = "Checking packaged native viewer…";
        Status.FontSize = 12;
        _viewer.StateChanged += (_, state) =>
            _position.Text = $"Page {state.Page} of {state.PageCount}  ·  {state.Zoom:P0}";
        _navigation.Children.Add(
            PdfAction(
                "Previous",
                async () => await _viewer.GoToPageAsync(Math.Max(1, (_viewer.State?.Page ?? 1) - 1), _operations.Token)
            )
        );
        _navigation.Children.Add(
            PdfAction(
                "Next",
                async () =>
                    await _viewer.GoToPageAsync(
                        Math.Min(_viewer.State?.PageCount ?? 1, (_viewer.State?.Page ?? 0) + 1),
                        _operations.Token
                    )
            )
        );
        _zoom.Children.Add(
            PdfAction(
                "−",
                async () => await _viewer.SetZoomAsync(Math.Max(1, (_viewer.State?.Zoom ?? 1) - 0.5), _operations.Token)
            )
        );
        _zoom.Children.Add(PdfAction("Fit page", async () => await _viewer.SetZoomAsync(1, _operations.Token)));
        _zoom.Children.Add(
            PdfAction(
                "+",
                async () => await _viewer.SetZoomAsync(Math.Min(4, (_viewer.State?.Zoom ?? 1) + 0.5), _operations.Token)
            )
        );
        _zoom.Children.Add(
            PdfAction(
                "Reopen",
                async () =>
                {
                    _viewer.Reconnect();
                    await OpenGuideAsync();
                }
            )
        );
        _layout = new Grid
        {
            Margin = new Thickness(16, 12),
            RowSpacing = 10,
            RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto,Auto,Auto"),
        };
        _layout.Children.Add(_position);
        AddRow(_viewer, 1);
        AddRow(_navigation, 2);
        AddRow(_zoom, 3);
        AddRow(
            new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = OperatingSystem.IsWindows()
                            ? "Use Previous and Next to turn pages. Zoom with +/− or pinch, then scroll to pan. Reopen creates a fresh native view."
                            : "Pinch to zoom, drag to pan. At fit, swipe to turn pages. Reopen releases the document and creates a fresh native view.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        FontSize = 12,
                    },
                    Status,
                },
            },
            4
        );
        AddRow(_probe, 5);
        Content = _layout;
        SetWorking(true);
        Loaded += async (_, _) =>
        {
            if (_started || SampleApplication.VerifyOnLoad)
                return;
            _started = true;
            _startup = InitializeAsync();
            await _startup;
        };
    }

    private void AddRow(Control control, int row)
    {
        Grid.SetRow(control, row);
        _layout.Children.Add(control);
    }

    private Button PdfAction(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) =>
        {
            if (_working || IsClosed)
                return;
            SetWorking(true);
            _action = Attempt(async () =>
            {
                await action();
                Status.Text = "Page and zoom state received from native code.";
            });
            try
            {
                await _action;
            }
            finally
            {
                SetWorking(IsClosed);
            }
        };
        return button;
    }

    private void SetWorking(bool value)
    {
        _working = value;
        _navigation.IsEnabled = _zoom.IsEnabled = !value;
    }

    public override Task<string> VerifyAsync() =>
        SharedPdfVerification.RunAsync([_viewer, _probe], _temporary, _operations.Token);

    private async Task InitializeAsync()
    {
        try
        {
            Console.WriteLine(await VerifyAsync());
            _operations.Token.ThrowIfCancellationRequested();
            await OpenGuideAsync();
            Status.Text = "Native document, navigation, callbacks and cleanup verified.";
            SetWorking(false);
        }
        catch (Exception error)
        {
            Status.Text = "FAIL " + error.Message;
        }
        finally
        {
            await _probe.DisconnectAsync();
            _layout.Children.Remove(_probe);
        }
    }

    private async Task OpenGuideAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_operations.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!_viewer.IsConnected)
            await Task.Delay(25, deadline.Token);
        if (OperatingSystem.IsBrowser())
        {
            using var input =
                typeof(PdfPage).Assembly.GetManifestResourceStream("Verification.pdf")
                ?? throw new InvalidOperationException("Missing verification PDF.");
            using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, deadline.Token);
            await _viewer.OpenBytesAsync(buffer.ToArray(), deadline.Token);
        }
        else
            await _viewer.OpenAsync(_temporary[0], deadline.Token);
    }

    public override async Task SuspendAsync()
    {
        if (!IsClosed)
            return;
        _operations.Cancel();
        await _startup;
        await _action;
        await _viewer.DisconnectAsync();
        await _probe.DisconnectAsync();
        foreach (var path in _temporary)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
        }
        _temporary.Clear();
        _operations.Dispose();
    }
}
