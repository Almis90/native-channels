using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using NativeSamples;
using Plugin.VolumeMonitor;

namespace VolumeMonitor.Sample;

public sealed class MainPage : ContentPage
{
    private readonly IVolumeMonitor _monitor;
    private readonly Label _level = new()
    {
        Text = "—",
        FontSize = 64,
        FontAttributes = FontAttributes.Bold,
    };
    private readonly Label _state = new() { Text = "Checking native subscriptions…" };
    private readonly Label _verification = new() { FontSize = 12 };
    private readonly Label _count = new() { Text = "No events received" };
    private readonly ProgressBar _bar = new() { ProgressColor = Color.FromArgb("6554C0"), HeightRequest = 12 };
    private readonly Button _start = new() { Text = "Start listening", IsEnabled = false };
    private readonly Button _stop = new() { Text = "Stop", IsEnabled = false };
    private readonly VerticalStackLayout _history = new() { Spacing = 8 };
    private CancellationTokenSource? _listening;
    private AudioSessionScope? _audioSession;
    private Task _run = Task.CompletedTask;
    private bool _appeared;
    private int _eventCount;

    public MainPage(IVolumeMonitor monitor)
    {
        _monitor = monitor;
        Title = "Volume Monitor";
        SafeAreaEdges = SafeAreaEdges.All;
        _start.Clicked += async (_, _) => await StartAsync();
        _stop.Clicked += async (_, _) => await StopAsync();
#if ANDROID
        const string platform =
            "Android media volume · Settings changes are observed through AudioManager. Delivery can vary by device.";
#elif WINDOWS
        const string platform =
            "Windows output volume · Core Audio observes the default multimedia output device and follows changes to that device.";
#elif MACCATALYST
        const string platform =
            "Mac output volume · Core Audio observes the default output device. Outputs without a volume control report an error.";
#else
        const string platform =
            "Apple output volume · KVO observes AVAudioSession. Use a physical iPhone to verify changes; the simulator may report only its initial level.";
#endif
        Content = new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Content = new VerticalStackLayout
            {
                SafeAreaEdges = SafeAreaEdges.None,
                Padding = 24,
                Spacing = 16,
                MaximumWidthRequest = 560,
                Children =
                {
                    new Label
                    {
                        Text = "Your volume. Live from native code.",
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label { Text = platform },
                    _level,
                    _bar,
                    _state,
                    new HorizontalStackLayout { Spacing = 12, Children = { _start, _stop } },
                    _count,
                    new Label
                    {
                        Text = "Recent events",
                        FontSize = 20,
                        FontAttributes = FontAttributes.Bold,
                    },
                    _history,
                    _verification,
                    new Label
                    {
                        Text =
                            "Change media volume with the device controls. This shows level changes, not every button press. Stop removes the native observer; backgrounding this app also stops listening.",
                        FontSize = 13,
                    },
                },
            },
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_appeared)
            return;
        _appeared = true;
        _listening = new();
        _run = VerifyAsync(_listening.Token);
        await _run;
        if (!LocalVerification.Enabled && !_listening.IsCancellationRequested)
            await StartAsync();
    }

    private async Task VerifyAsync(CancellationToken cancellation)
    {
        string result;
        try
        {
            EnsureAudioSession();
            result = await VolumeVerification.RunAsync(_monitor, cancellation);
            _verification.Text =
                "Native initial levels, independent listeners, cancellation and restart passed. Hardware changes are verified separately.";
        }
        catch (Exception error)
        {
            result = "FAIL " + error;
            _verification.Text = "Native check failed: " + error.Message;
        }
        Console.WriteLine("VOLUME_SAMPLE " + result);
        await File.WriteAllTextAsync(
            (
                Environment.GetEnvironmentVariable("VOLUME_SAMPLE_RESULT")
                ?? Path.Combine(FileSystem.AppDataDirectory, "volume-result.txt")
            ),
            result
        );
        if (
            await LocalVerification.CompleteAsync(
                this,
                result,
                () =>
                {
                    _audioSession?.Dispose();
                    _audioSession = null;
                    return Task.CompletedTask;
                }
            )
        )
            return;
        _state.Text = "Ready";
        _start.IsEnabled = true;
    }

    private async Task StartAsync()
    {
        if (!_run.IsCompleted)
            return;
        _listening?.Dispose();
        _listening = new();
        _start.IsEnabled = false;
        _stop.IsEnabled = true;
        _run = ListenAsync(_listening.Token);
        await _run;
    }

    private async Task ListenAsync(CancellationToken cancellation)
    {
        try
        {
            EnsureAudioSession();
            _state.Text = "Listening";
            Console.WriteLine("VOLUME_SAMPLE STATE Listening");
            await foreach (var volume in _monitor.ListenAsync(cancellation))
            {
                _level.Text = volume.ToString("P0");
                _ = _bar.ProgressTo(volume, 180, Easing.CubicOut);
                _count.Text = $"{++_eventCount} events received · last update {DateTime.Now:T}";
                _history.Children.Insert(
                    0,
                    new Label { Text = $"{DateTime.Now:HH:mm:ss}   {volume:P0}", FontSize = 14 }
                );
                if (_history.Children.Count > 12)
                    _history.Children.RemoveAt(12);
                Console.WriteLine($"VOLUME_SAMPLE EVENT {volume:R}");
            }
            _state.Text = "Stream completed";
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Stopped";
        }
        catch (Exception error)
        {
            _state.Text = error.Message;
        }
        finally
        {
            _bar.CancelAnimations();
            _start.IsEnabled = true;
            _stop.IsEnabled = false;
            Console.WriteLine("VOLUME_SAMPLE STATE " + _state.Text);
        }
    }

    public async Task StopAsync()
    {
        _listening?.Cancel();
        await _run;
    }

    public void Resume()
    {
        try
        {
            EnsureAudioSession();
        }
        catch (Exception error)
        {
            _state.Text = error.Message;
        }
    }

    public async Task SuspendAsync()
    {
        var stopping = StopAsync();
        var session = _audioSession;
        _audioSession = null;
        try
        {
            // Release before awaiting so a quick resume cannot lose its new session.
            session?.Dispose();
        }
        catch (Exception error)
        {
            _state.Text = error.Message;
        }
        await stopping;
    }

    private void EnsureAudioSession()
    {
        // Keep iOS volume state current between subscriptions while the demo is foregrounded.
        _audioSession ??= new AudioSessionScope();
    }
}
