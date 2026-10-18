using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using NativeChannels;
using Plugin.Avalonia.PdfViewer;

namespace ReferenceSamples;

internal sealed class PdfPage : SamplePage
{
    private readonly List<PdfViewer> _views = [];
    private readonly List<string> _temporary = [];
    private readonly CancellationTokenSource _operations = new();

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
        var panel = new Grid { Margin = new Thickness(12), RowDefinitions = new RowDefinitions("Auto,Auto,*,*") };
        panel.Children.Add(new TextBlock { Text = "Two independent native PDF views", FontSize = 24 });
        Grid.SetRow(Status, 1);
        panel.Children.Add(Status);
        for (var i = 0; i < 2; i++)
        {
            var view = new PdfViewer();
            _views.Add(view);
            var state = new TextBlock { Text = "Connecting…" };
            view.Connected += (_, _) => state.Text = "Ready — open a local PDF";
            view.Disconnected += (_, _) => state.Text = "Detached";
            view.StateChanged += (_, value) =>
                state.Text = $"Page {value.Page}/{value.PageCount} · {value.Zoom:F1}× · {value.InstanceId[..8]}";
            var buttons = new WrapPanel
            {
                Children =
                {
                    Button("Open", () => Open(view)),
                    Button(
                        "Previous",
                        async () =>
                            await view.GoToPageAsync(Math.Max(1, (view.State?.Page ?? 1) - 1), _operations.Token)
                    ),
                    Button(
                        "Next",
                        async () =>
                            await view.GoToPageAsync(
                                Math.Min(view.State?.PageCount ?? 1, (view.State?.Page ?? 0) + 1),
                                _operations.Token
                            )
                    ),
                    Button(
                        "Zoom +",
                        async () =>
                            await view.SetZoomAsync(Math.Min(4, (view.State?.Zoom ?? 1) + 0.5), _operations.Token)
                    ),
                    Button("Fit", async () => await view.SetZoomAsync(1, _operations.Token)),
                    Button("Detach", view.DisconnectAsync),
                    Button(
                        "Reconnect",
                        () =>
                        {
                            view.Reconnect();
                            return Task.CompletedTask;
                        }
                    ),
                },
            };
            var section = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(0, 8) };
            section.Children.Add(buttons);
            Grid.SetRow(state, 1);
            section.Children.Add(state);
            Grid.SetRow(view, 2);
            section.Children.Add(view);
            Grid.SetRow(section, i + 2);
            panel.Children.Add(section);
        }
        Content = panel;
    }

    private async Task Open(PdfViewer view)
    {
        var files = await TopLevel
            .GetTopLevel(this)!
            .StorageProvider.OpenFilePickerAsync(
                new()
                {
                    Title = "Open PDF",
                    AllowMultiple = false,
                    FileTypeFilter = [new("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }],
                }
            );
        if (files.Count == 0 || IsClosed)
            return;
        using var file = files[0];
        if (OperatingSystem.IsBrowser())
        {
            await using var input = await file.OpenReadAsync();
            using var bytes = new MemoryStream();
            var buffer = new byte[65536];
            int read;
            while ((read = await input.ReadAsync(buffer, _operations.Token)) != 0)
            {
                if (bytes.Length + read > 8 * 1024 * 1024)
                    throw new InvalidDataException("Select a PDF up to 8 MiB.");
                bytes.Write(buffer, 0, read);
            }
            await view.OpenBytesAsync(bytes.ToArray(), _operations.Token);
        }
        else
        {
            var path = file.TryGetLocalPath();
            if (path is null)
            {
                path = Path.Combine(Path.GetTempPath(), $"nativechannels-{Guid.NewGuid():N}.pdf");
                _temporary.Add(path);
                await using var input = await file.OpenReadAsync();
                await using (var output = File.Create(path))
                    await input.CopyToAsync(output, _operations.Token);
            }
            await view.OpenAsync(path, _operations.Token);
        }
    }

    public override async Task SuspendAsync()
    {
        // Hidden views retain their document; final disposal disconnects before deleting owned files.
        if (!IsClosed)
            return;
        _operations.Cancel();
        foreach (var view in _views)
            await view.DisconnectAsync();
        foreach (var path in _temporary)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException) { }
        }
        _temporary.Clear();
    }
}
