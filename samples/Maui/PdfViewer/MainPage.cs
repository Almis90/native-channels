using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using Plugin.Maui.PdfViewer;
using DocumentViewer = Plugin.Maui.PdfViewer.PdfViewer;

namespace PdfViewer.Sample;

public sealed class MainPage : ContentPage
{
    private readonly DocumentViewer _viewer = new();
    private readonly DocumentViewer _probe = new()
    {
        HeightRequest = 1,
        Opacity = 0,
        InputTransparent = true,
    };
    private readonly Label _position = new()
    {
        Text = "Opening document…",
        HorizontalTextAlignment = TextAlignment.Center,
        TextColor = Color.FromArgb("12263A"),
    };
    private readonly Label _status = new()
    {
        Text = "Checking packaged native viewer…",
        FontSize = 12,
        TextColor = Color.FromArgb("475569"),
    };
    private readonly HorizontalStackLayout _navigation = new()
    {
        Spacing = 8,
        HorizontalOptions = LayoutOptions.Center,
    };
    private readonly HorizontalStackLayout _zoom = new() { Spacing = 8, HorizontalOptions = LayoutOptions.Center };
    private readonly Grid _layout;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _probeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private string? _path;
    private bool _started;
    private bool _working = true;
    private int _page = 1;
    private int _count;
    private double _factor = 1;

    public MainPage()
    {
        Title = "PDF Viewer";
        SafeAreaEdges = SafeAreaEdges.All;
        BackgroundColor = Color.FromArgb("F5F7FB");
        _viewer.Connected += (_, _) => _ready.TrySetResult();
        _probe.Connected += (_, _) => _probeReady.TrySetResult();
        _viewer.StateChanged += (_, state) => ShowState(state);
        _navigation.Children.Add(Action("Previous", () => _viewer.GoToPageAsync(Math.Max(1, _page - 1))));
        _navigation.Children.Add(Action("Next", () => _viewer.GoToPageAsync(Math.Min(_count, _page + 1))));
        _zoom.Children.Add(Action("−", () => _viewer.SetZoomAsync(Math.Max(1, _factor - 0.5))));
        _zoom.Children.Add(Action("Fit page", () => _viewer.SetZoomAsync(1)));
        _zoom.Children.Add(Action("+", () => _viewer.SetZoomAsync(Math.Min(4, _factor + 0.5))));
        _zoom.Children.Add(
            Action(
                "Reopen",
                async () =>
                {
                    _viewer.Reconnect();
                    return await _viewer.OpenAsync(_path!);
                }
            )
        );
        _layout = new Grid
        {
            Padding = new Thickness(16, 12),
            RowSpacing = 10,
            RowDefinitions =
            [
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto),
            ],
        };
        _layout.Add(_position, 0, 0);
        _layout.Add(_viewer, 0, 1);
        _layout.Add(_navigation, 0, 2);
        _layout.Add(_zoom, 0, 3);
        _layout.Add(
            new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    new Label
                    {
                        Text =
#if WINDOWS
                            "Use Previous and Next to turn pages. Zoom with +/− or pinch, then scroll to pan. Reopen creates a fresh native view.",
#else
                            "Pinch to zoom, drag to pan. At fit, swipe to turn pages. Reopen releases the document and creates a fresh native view.",
#endif
                        FontSize = 12,
                        TextColor = Color.FromArgb("475569"),
                    },
                    _status,
                },
            },
            0,
            4
        );
        _layout.Add(_probe, 0, 5);
        Content = _layout;
        SetWorking(true);
    }

    private Button Action(string title, Func<Task<PdfState>> action)
    {
        var button = new Button
        {
            Text = title,
            BackgroundColor = Color.FromArgb("12263A"),
            TextColor = Colors.White,
            FontSize = 13,
            Padding = new Thickness(14, 8),
            CornerRadius = 10,
        };
        button.Clicked += async (_, _) =>
        {
            if (_working)
                return;
            SetWorking(true);
            try
            {
                ShowState(await action());
                _status.Text = "Page and zoom state received from native code.";
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
            }
            finally
            {
                SetWorking(false);
            }
        };
        return button;
    }

    private void ShowState(PdfState state)
    {
        _page = state.Page;
        _count = state.PageCount;
        _factor = state.Zoom;
        _position.Text = $"Page {_page} of {_count}  ·  {_factor:P0}";
    }

    private void SetWorking(bool value)
    {
        _working = value;
        _navigation.IsEnabled = !value;
        _zoom.IsEnabled = !value;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
            return;
        _started = true;
        string report;
        try
        {
            await Task.WhenAll(_ready.Task, _probeReady.Task).WaitAsync(TimeSpan.FromSeconds(30));
            await using (var source = await FileSystem.OpenAppPackageFileAsync("nativechannels-guide.pdf"))
            {
                _path = Path.Combine(FileSystem.AppDataDirectory, "nativechannels-guide.pdf");
                await using var target = File.Create(_path);
                await source.CopyToAsync(target);
            }
            report = "PASS " + await PdfVerification.RunAsync(_viewer, _probe, _path);
            ShowState(await _viewer.OpenAsync(_path));
            _status.Text = "Native document, navigation, callbacks and cleanup verified.";
            SetWorking(false);
        }
        catch (Exception error)
        {
            report = "FAIL " + error;
            _status.Text = report;
        }
        finally
        {
            await _probe.DisconnectAsync();
            _layout.Remove(_probe);
        }
        Console.WriteLine("PDF_SAMPLE " + report);
        File.WriteAllText(
            (
                Environment.GetEnvironmentVariable("PDF_SAMPLE_RESULT")
                ?? Path.Combine(FileSystem.AppDataDirectory, "pdf-result.txt")
            ),
            report
        );
    }
}
