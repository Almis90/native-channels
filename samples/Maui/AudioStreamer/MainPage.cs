using System.Diagnostics;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using NativeSamples;
using Plugin.Maui.AudioStreamer;

namespace AudioStreamer.Sample;

public sealed class MainPage : ContentPage
{
    private readonly IAudioStreamer _audio;
    private readonly Label _state = new()
    {
        Text = "Ready",
        FontSize = 24,
        FontAttributes = FontAttributes.Bold,
    };
    private readonly Label _bufferText = new() { Text = "Native buffer: 0 / 500 ms" };
    private readonly ProgressBar _buffer = new() { ProgressColor = Color.FromArgb("6554C0") };
    private readonly Label _metrics = new() { Text = "48,000 Hz · mono · PCM16 little-endian" };
    private readonly Label _verification = new() { Text = "Checking native playback…", FontSize = 12 };
    private readonly Slider _volume = new(0, 0.25, 0.16);
    private readonly Button _short = new() { Text = "Play 16-second music", IsEnabled = false };
    private readonly Button _continuous = new() { Text = "Loop music", IsEnabled = false };
    private readonly Button _pause = new() { Text = "Pause", IsEnabled = false };
    private readonly Button _stop = new() { Text = "Stop", IsEnabled = false };
    private CancellationTokenSource? _playback;
    private AudioStreamSession? _session;
    private Task _run = Task.CompletedTask;
    private bool _started;
    private bool _paused;
    private double _selectedVolume = 0.16;

    public MainPage(IAudioStreamer audio)
    {
        _audio = audio;
        Title = "Audio Streamer";
        SafeAreaEdges = SafeAreaEdges.All;
        _short.Clicked += async (_, _) => await StartAsync(false);
        _continuous.Clicked += async (_, _) => await StartAsync(true);
        _pause.Clicked += async (_, _) => await TogglePauseAsync();
        _stop.Clicked += async (_, _) => await StopAsync();
        var volume = new Label { Text = "Signal level: 16%" };
        _volume.ValueChanged += (_, e) =>
        {
            _selectedVolume = e.NewValue;
            volume.Text = $"Signal level: {e.NewValue:P0}";
        };
        Content = new ScrollView
        {
            SafeAreaEdges = SafeAreaEdges.None,
            Content = new VerticalStackLayout
            {
                SafeAreaEdges = SafeAreaEdges.None,
                Padding = 24,
                Spacing = 16,
                MaximumWidthRequest = 560,
                HorizontalOptions = LayoutOptions.Fill,
                Children =
                {
                    new Label
                    {
                        Text = "A little music. Native playback.",
                        FontSize = 28,
                        FontAttributes = FontAttributes.Bold,
                    },
                    new Label
                    {
                        Text =
                            "An original piano-style melody with bass and a soft beat, generated in C# and streamed as PCM to the native player.",
                    },
                    _state,
                    new Label { Text = "Sunny Steps · 120 BPM · original instrumental" },
                    volume,
                    _volume,
                    _short,
                    _continuous,
                    new HorizontalStackLayout { Spacing = 12, Children = { _pause, _stop } },
                    _bufferText,
                    _buffer,
                    _metrics,
                    _verification,
                    new Label
                    {
                        Text =
                            "The buffer holds at most half a second of audio. When it fills, the sender waits. Pause keeps buffered audio; Stop clears it. Playback stops when this app backgrounds. Adjust the device’s media volume if playback is quiet.",
                        FontSize = 13,
                    },
                },
            },
        };
    }

#if !ANDROID
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await VerifyPlaybackAsync();
    }
#endif

    internal async Task VerifyPlaybackAsync()
    {
        if (_started)
            return;
        _started = true;
        _playback = new();
        _run = VerifyAsync();
        await _run;
    }

    private async Task VerifyAsync()
    {
        string result;
        try
        {
            result = await AudioVerification.RunAsync(_audio, _playback!.Token);
            _verification.Text = "Native checks passed: playback, buffering, pause, cancellation, drain and restart.";
        }
        catch (Exception error)
        {
            result = "FAIL " + error;
            _verification.Text = "Native check failed: " + error.Message;
        }
        Console.WriteLine("AUDIO_SAMPLE " + result);
        await File.WriteAllTextAsync(
            (
                Environment.GetEnvironmentVariable("AUDIO_SAMPLE_RESULT")
                ?? Path.Combine(FileSystem.AppDataDirectory, "audio-result.txt")
            ),
            result
        );
        if (await LocalVerification.CompleteAsync(this, result, () => _audio.DisposeAsync().AsTask()))
            return;
        _short.IsEnabled = _continuous.IsEnabled = true;
    }

    private async Task StartAsync(bool continuous)
    {
        if (!_run.IsCompleted)
            return;
        _playback?.Dispose();
        _playback = new();
        _short.IsEnabled = _continuous.IsEnabled = false;
        _stop.IsEnabled = true;
        _run = PlayAsync(continuous, _playback.Token);
        await _run;
    }

    private async Task PlayAsync(bool continuous, CancellationToken cancellation)
    {
        Task monitor = Task.CompletedTask;
        using var monitoring = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            _state.Text = "Opening native audio…";
            _session = await _audio.OpenAsync(cancellation);
            _paused = false;
            _pause.Text = "Pause";
            _pause.IsEnabled = true;
            _state.Text = "Playing";
            monitor = MonitorAsync(_session, monitoring.Token);
            var generator = new MusicGenerator();
            var count = 0;
            var totalWait = TimeSpan.Zero;
            const int framesPerChunk = 2400; // 50 ms leaves headroom for emulator bridge latency.
            const int chunksPerLoop = MusicGenerator.LoopFrames / framesPerChunk;
            while (continuous || count < chunksPerLoop)
            {
                cancellation.ThrowIfCancellationRequested();
                var chunk = generator.Next(framesPerChunk, _selectedVolume, !continuous && count == chunksPerLoop - 1);
                var clock = Stopwatch.StartNew();
                await _session.WriteAsync(chunk, cancellation);
                totalWait += clock.Elapsed;
                count++;
                _metrics.Text =
                    $"{count * framesPerChunk * 2:N0} PCM bytes sent · mean enqueue wait {totalWait.TotalMilliseconds / count:F1} ms";
            }
            await _session.DrainAsync(cancellation);
            _state.Text = "Completed";
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Stopped";
        }
        catch (ObjectDisposedException)
        {
            _state.Text = "Stopped";
        }
        catch (Exception error)
        {
            _state.Text = error.Message;
        }
        finally
        {
            monitoring.Cancel();
            try
            {
                await monitor;
            }
            catch (OperationCanceledException) { }
            var session = _session;
            _session = null;
            if (session is not null)
            {
                try
                {
                    await session.DisposeAsync();
                }
                catch (Exception error)
                {
                    _state.Text = error.Message;
                }
            }
            _buffer.Progress = 0;
            _bufferText.Text = "Native buffer: 0 / 500 ms";
            _pause.IsEnabled = _stop.IsEnabled = false;
            _short.IsEnabled = _continuous.IsEnabled = true;
        }
    }

    private async Task MonitorAsync(AudioStreamSession session, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                var status = await session.GetStatusAsync(cancellation);
                _buffer.Progress = status.BufferUtilization;
                _bufferText.Text =
                    $"Native buffer: {status.BufferedDuration.TotalMilliseconds:F0} / 500 ms · played {status.PlayedFrames / 48000.0:F1} s";
                await Task.Delay(100, cancellation);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception error)
        {
            _state.Text = error.Message;
            _playback?.Cancel();
        }
    }

    private async Task TogglePauseAsync()
    {
        if (_session is null)
            return;
        _pause.IsEnabled = false;
        try
        {
            if (_paused)
                await _session.ResumeAsync();
            else
                await _session.PauseAsync();
            _paused = !_paused;
            _pause.Text = _paused ? "Resume" : "Pause";
            _state.Text = _paused ? "Paused" : "Playing";
        }
        catch (Exception error)
        {
            _state.Text = error.Message;
        }
        finally
        {
            _pause.IsEnabled = _session is not null;
        }
    }

    public async Task StopAsync()
    {
        _playback?.Cancel();
        if (_session is { } session)
        {
            try
            {
                await session.DisposeAsync();
            }
            catch (Exception error)
            {
                _state.Text = error.Message;
            }
        }
        await _run;
    }
}
