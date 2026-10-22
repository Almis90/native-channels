using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeChannels;
using Plugin.AudioStreamer;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "AudioStreamer";
    private const string Limitation =
        "Windows, macOS ARM64 desktop, Linux x64 X11, iOS ARM64 simulator and Android x64 emulator silent transport verified; listening and other targets pending.";
    private AudioStreamSession? _playback;
    private CancellationTokenSource? _playing;
    private Task _producer = Task.CompletedTask;
    private Task _suspension = Task.CompletedTask;
    private Exception? _playbackError;
    private bool _paused;
    private readonly Slider _level = new()
    {
        Minimum = 0,
        Maximum = 0.25,
        StepFrequency = 0.01,
        Value = 0.16,
    };
    private readonly TextBlock _buffer = new();
    private IAudioStreamer Audio =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddAudioStreamer());
            services.AddAudioStreamer();
        })
            .Services.GetRequiredService<IAudioStreamer>();

    private void Build()
    {
#if UNO_BROWSER_SAMPLE
        // Match Avalonia's eager session setup so the adapter can capture Play's user gesture.
        _ = Audio;
#endif
        ((TextBlock)_panel.Children[0]).Text = "Sunny Steps";
        _panel.Children.Insert(
            1,
            new TextBlock { Text = "Original 16-second music · 48 kHz mono PCM16", TextWrapping = TextWrapping.Wrap }
        );
        _panel.Children.Remove(_surface);
        _status.Text = "Ready. Select Play to enable audio.";
        _panel.Children.Add(new TextBlock { Text = "Signal level (0–25%)" });
        _panel.Children.Add(_level);
        Button("Play", () => StartAsync());
        Button("Loop", () => StartAsync(loop: true));
        Button("Pause / resume", () => _paused ? ResumeAsync() : PauseAsync());
        if (OperatingSystem.IsWindows())
            _window.AppWindow.Changed += (_, _) =>
            {
                if (
                    _window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
                    {
                        State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized
                    }
                )
                    _suspension = SuspendAsync();
            };
        Button("Stop", StopAsync);
        _panel.Children.Add(_buffer);
        _panel.Children.Add(
            new TextBlock
            {
                Text =
                    "Playback stops when the app is hidden, minimized or closed. Browser playback requires a click and uses Web Audio.",
                TextWrapping = TextWrapping.Wrap,
            }
        );
    }

    private async Task StartAsync(bool silent = false, bool loop = false)
    {
        if (!_producer.IsCompleted)
            return;
        await StopAsync();
        _paused = false;
        _playbackError = null;
        _playback = await Audio.OpenAsync(_stopping.Token);
        _playing = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        _producer = ProduceAsync(_playback, silent, loop, _playing.Token);
    }

    private async Task PauseAsync()
    {
        if (_playback is not null)
        {
            await _playback.PauseAsync(_stopping.Token);
            _paused = true;
            _status.Text = "Paused";
        }
    }

    private async Task ResumeAsync()
    {
        if (_playback is not null)
        {
            await _playback.ResumeAsync(_stopping.Token);
            _paused = false;
            _status.Text = "Playing";
        }
    }

    private async Task ProduceAsync(AudioStreamSession playback, bool silent, bool loop, CancellationToken token)
    {
        using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var monitor = MonitorAsync(playback, monitorStop.Token);
        try
        {
            var music = new global::AudioStreamer.Sample.MusicGenerator();
            _paused = false;
            _status.Text = "Playing";
            const int frames = 2400;
            var chunks = global::AudioStreamer.Sample.MusicGenerator.LoopFrames / frames;
            for (int i = 0; loop || i < chunks; i++)
            {
                token.ThrowIfCancellationRequested();
                await playback.WriteAsync(
                    music.Next(frames, silent ? 0 : _level.Value, !loop && i == chunks - 1),
                    token
                );
            }
            await playback.DrainAsync(token);
            _status.Text = "Completed";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _playbackError = error;
            _status.Text = error.Message;
        }
        finally
        {
            monitorStop.Cancel();
            await monitor;
            await playback.DisposeAsync();
            if (ReferenceEquals(_playback, playback))
                _playback = null;
            _buffer.Text = "Buffer: 0 / 500 ms";
        }
    }

    private async Task MonitorAsync(AudioStreamSession playback, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var state = await playback.GetStatusAsync(token);
                _buffer.Text =
                    $"Buffer: {state.BufferedDuration.TotalMilliseconds:F0} / 500 ms · played {state.PlayedFrames / 48000.0:F1}s";
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _playbackError = error;
            _status.Text = error.Message;
            _playing?.Cancel();
        }
    }

    private async Task VerifyAsync()
    {
        Console.WriteLine(await global::AudioStreamer.Sample.AudioVerification.RunAsync(Audio, _stopping.Token));
#if !UNO_BROWSER_SAMPLE
        await global::AudioStreamer.Avalonia.AvaloniaAudioVerification.RunAsync(_host!, _window, _stopping.Token);
        await VerifyControlsAsync();
#endif
    }

    private async Task VerifyControlsAsync()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        await StartAsync(silent: true);
        await PauseAsync();
        Require((await _playback!.GetStatusAsync(deadline.Token)).State == "paused", "pause control");
        await ResumeAsync();
        Require((await _playback.GetStatusAsync(deadline.Token)).State == "playing", "resume control");
        if (OperatingSystem.IsWindows())
        {
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)_window.AppWindow.Presenter;
            try
            {
                presenter.Minimize();
                await _producer.WaitAsync(deadline.Token);
                await _suspension.WaitAsync(deadline.Token);
                Require(_playback is null && _playbackError is null, "minimize stops playback");
            }
            finally
            {
                presenter.Restore();
            }
        }
        else
        {
            await SuspendAsync();
        }
        await StartAsync(silent: true);
        await StopAsync();
        Require(_playback is null && _playbackError is null, "stop control");
        await StartAsync(silent: true);
        await PauseAsync();
        Require(!_producer.IsCompleted && _playbackError is null, "cleanup with active paused playback");
        // The shared sample runner disposes this page and closes its real window.
        var lifecycle = OperatingSystem.IsWindows() ? "minimize/restart" : "suspend/restart";
        Console.WriteLine($"PASS host disposal, pending writes, three host sessions, controls and {lifecycle}");
    }

    public async Task StopAsync()
    {
        var playing = _playing;
        _playing = null;
        playing?.Cancel();
        await _producer;
        if (_playback is not null)
            await _playback.DisposeAsync();
        _playback = null;
        playing?.Dispose();
        _paused = false;
        _status.Text = "Stopped";
    }
}
