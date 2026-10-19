using System.Diagnostics;
using AudioStreamer.Sample;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;

namespace AudioStreamer.Avalonia;

public sealed class MainWindow : Window
{
    private readonly AvaloniaChannelSession _owner;
    private readonly IAudioStreamer _audio;
    private readonly CancellationTokenSource _lifetime;
    private readonly TextBlock _state = new() { Text = "Checking native playback…", FontSize = 24 };
    private readonly TextBlock _bufferText = new() { Text = "Native buffer: 0 / 500 ms" };
    private readonly ProgressBar _buffer = new() { Maximum = 1 };
    private readonly TextBlock _metrics = new()
    {
        Text = "48,000 Hz · mono · PCM16 little-endian",
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _verification = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly Slider _volume = new()
    {
        Minimum = 0,
        Maximum = 0.25,
        Value = 0.16,
    };
    private readonly Button _short = new() { Content = "Play 16-second music", IsEnabled = false };
    private readonly Button _continuous = new() { Content = "Loop music", IsEnabled = false };
    private readonly Button _pause = new() { Content = "Pause", IsEnabled = false };
    private readonly Button _stop = new() { Content = "Stop", IsEnabled = false };
    private CancellationTokenSource? _playback;
    private AudioStreamSession? _session;
    private Task _run = Task.CompletedTask;
    private Task _pauseOperation = Task.CompletedTask;
    private Task? _initialization;
    private Task? _close;
    private int _stopping;
    private bool _ready;
    private bool _closing;
    private bool _paused;
    private double _level = 0.16;
    private Exception? _playbackError;

    public MainWindow(AvaloniaChannelSession owner)
    {
        _owner = owner;
        _audio = owner.Services.GetRequiredService<IAudioStreamer>();
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(owner.Stopping);
        Title = "Audio Streamer — Avalonia";
        Width = 620;
        Height = 740;
        MinWidth = 360;
        MinHeight = 480;
        _short.Click += async (_, _) => await StartAsync(false);
        _continuous.Click += async (_, _) => await StartAsync(true);
        _pause.Click += async (_, _) => await TogglePauseAsync();
        _stop.Click += async (_, _) => await StopAsync();
        var level = new TextBlock { Text = "Signal level: 16%" };
        _volume.PropertyChanged += (_, args) =>
        {
            if (args.Property != Slider.ValueProperty)
                return;
            _level = _volume.Value;
            level.Text = $"Signal level: {_level:P0}";
        };
        PropertyChanged += async (_, args) =>
        {
            if (args.Property == WindowStateProperty && WindowState == WindowState.Minimized)
                await StopAsync();
        };
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(28),
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text = "A little music. Native playback.",
                        FontSize = 28,
                        FontWeight = FontWeight.Bold,
                        TextWrapping = TextWrapping.Wrap,
                    },
                    new TextBlock
                    {
                        Text = "An original piano-style melody with bass and a soft beat, streamed to your speakers.",
                        TextWrapping = TextWrapping.Wrap,
                    },
                    _state,
                    new TextBlock { Text = "Sunny Steps · 120 BPM · original instrumental" },
                    level,
                    _volume,
                    _short,
                    _continuous,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 12,
                        Children = { _pause, _stop },
                    },
                    _bufferText,
                    _buffer,
                    _metrics,
                    _verification,
                    new TextBlock
                    {
                        Text =
                            "Pause keeps buffered audio; Stop clears it. Playback stops when the window is minimized or closed. Adjust the device’s media volume if playback is quiet.",
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                    },
                },
            },
        };
    }

    internal Task<string> InitializeAsync(AvaloniaChannelHost host, bool verifyControls)
    {
        var initialization = InitializeCoreAsync(host, verifyControls);
        _initialization = initialization;
        return initialization;
    }

    private async Task<string> InitializeCoreAsync(AvaloniaChannelHost host, bool verifyControls)
    {
        var result = await AudioVerification.RunAsync(_audio, _lifetime.Token);
        _verification.Text = result;
        _state.Text = "Ready";
        if (verifyControls)
            await AvaloniaAudioVerification.RunAsync(host, _lifetime.Token);
        _ready = true;
        UpdateButtons();
        if (verifyControls)
        {
            await VerifyControlsAsync();
            result += "; Avalonia DI disposal, pending writes, host recreation, controls and minimize/restart";
        }
        return result;
    }

    private Task StartAsync(bool continuous)
    {
        if (!_ready || _closing || _stopping != 0 || !_run.IsCompleted)
            return Task.CompletedTask;
        _playback?.Dispose();
        _playback = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _playbackError = null;
        _run = PlayAsync(continuous, _playback.Token);
        UpdateButtons();
        return _run;
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
            _pause.Content = "Pause";
            _pause.IsEnabled = true;
            _state.Text = "Playing";
            monitor = MonitorAsync(_session, monitoring.Token);
            var generator = new MusicGenerator();
            var count = 0;
            var totalWait = TimeSpan.Zero;
            const int frames = 2400;
            const int chunks = MusicGenerator.LoopFrames / frames;
            while (continuous || count < chunks)
            {
                cancellation.ThrowIfCancellationRequested();
                var pcm = generator.Next(frames, _level, !continuous && count == chunks - 1);
                var clock = Stopwatch.StartNew();
                await _session.WriteAsync(pcm, cancellation);
                totalWait += clock.Elapsed;
                count++;
                _metrics.Text =
                    $"{count * frames * 2:N0} PCM bytes sent · mean enqueue wait {totalWait.TotalMilliseconds / count:F1} ms";
            }
            await _session.DrainAsync(cancellation);
            _state.Text = "Completed";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _state.Text = "Stopped";
        }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested)
        {
            _state.Text = "Stopped";
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        finally
        {
            monitoring.Cancel();
            await monitor;
            await _pauseOperation;
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
                    ShowError(error);
                }
            }
            _buffer.Value = 0;
            _bufferText.Text = "Native buffer: 0 / 500 ms";
            // This method's task completes after its finally block; enable Start explicitly.
            _pause.IsEnabled = _stop.IsEnabled = false;
            _short.IsEnabled = _continuous.IsEnabled = _ready && !_closing && _stopping == 0;
        }
    }

    private async Task MonitorAsync(AudioStreamSession session, CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                var status = await session.GetStatusAsync(cancellation);
                _buffer.Value = status.BufferUtilization;
                _bufferText.Text =
                    $"Native buffer: {status.BufferedDuration.TotalMilliseconds:F0} / 500 ms · played {status.PlayedFrames / 48000.0:F1} s";
                await Task.Delay(100, cancellation);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            ShowError(error);
            _playback?.Cancel();
        }
    }

    private Task TogglePauseAsync()
    {
        if (_session is null || _closing || _stopping != 0 || !_pauseOperation.IsCompleted)
            return Task.CompletedTask;
        return _pauseOperation = TogglePauseCoreAsync(_session);
    }

    private async Task TogglePauseCoreAsync(AudioStreamSession session)
    {
        _pause.IsEnabled = false;
        try
        {
            if (_paused)
                await session.ResumeAsync(_playback!.Token);
            else
                await session.PauseAsync(_playback!.Token);
            _paused = !_paused;
            _pause.Content = _paused ? "Resume" : "Pause";
            _state.Text = _paused ? "Paused" : "Playing";
        }
        catch (OperationCanceledException) when (_playback!.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (_playback!.IsCancellationRequested) { }
        catch (Exception error)
        {
            ShowError(error);
        }
        finally
        {
            _pause.IsEnabled = _session is not null && !_closing && _stopping == 0;
        }
    }

    private async Task StopAsync()
    {
        _stopping++;
        UpdateButtons();
        try
        {
            _playback?.Cancel();
            await _pauseOperation;
            await _run;
        }
        finally
        {
            _stopping--;
            UpdateButtons();
        }
    }

    internal Task CloseAsync() => _close ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        _closing = true;
        _lifetime.Cancel();
        try
        {
            await StopAsync();
            if (_initialization is not null)
            {
                try
                {
                    await _initialization;
                }
                catch (OperationCanceledException) when (_closing) { }
            }
            if (_playbackError is not null)
                throw new InvalidOperationException("Playback failed.", _playbackError);
        }
        finally
        {
            try
            {
                await _owner.DisposeAsync();
            }
            finally
            {
                _playback?.Dispose();
                _lifetime.Dispose();
            }
        }
    }

    internal void ShowError(Exception error)
    {
        _playbackError = error;
        _state.Text = "Audio error: " + error.Message;
    }

    private void UpdateButtons()
    {
        var idle = _run.IsCompleted;
        _short.IsEnabled = _continuous.IsEnabled = _ready && idle && !_closing && _stopping == 0;
        _stop.IsEnabled = !idle && !_closing && _stopping == 0;
        _pause.IsEnabled = _session is not null && _pauseOperation.IsCompleted && !_closing && _stopping == 0;
    }

    private async Task VerifyControlsAsync()
    {
        // Exercise the same commands used by the buttons, without playing audible music.
        _volume.Value = 0;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        _ = StartAsync(true);
        await WaitForPlaybackAsync(deadline.Token);
        await TogglePauseAsync();
        AvaloniaAudioVerification.Check(
            (await _session!.GetStatusAsync(deadline.Token)).State == "paused",
            "pause control"
        );
        await TogglePauseAsync();
        AvaloniaAudioVerification.Check(
            (await _session.GetStatusAsync(deadline.Token)).State == "playing",
            "resume control"
        );
        WindowState = WindowState.Minimized;
        // Minimize's event must finish the playback operation without a direct Stop call here.
        await _run.WaitAsync(deadline.Token);
        AvaloniaAudioVerification.Check(_session is null && _playbackError is null, "minimize stops playback");
        WindowState = WindowState.Normal;
        // Let the event's StopAsync continuation finish before starting another operation.
        while (_stopping != 0)
            await Task.Delay(10, deadline.Token);
        _ = StartAsync(true);
        await WaitForPlaybackAsync(deadline.Token);
        await StopAsync();
        AvaloniaAudioVerification.Check(_session is null && _playbackError is null, "stop control");
        _ = StartAsync(true);
        await WaitForPlaybackAsync(deadline.Token);
        await TogglePauseAsync();
        AvaloniaAudioVerification.Check(
            !_run.IsCompleted && _playbackError is null,
            "close with active paused playback"
        );
        // App now closes the real window and awaits this pending playback before shutting down its host.
    }

    private async Task WaitForPlaybackAsync(CancellationToken cancellation)
    {
        while (_session is null)
        {
            AvaloniaAudioVerification.Check(!_run.IsCompleted, "playback starts");
            await Task.Delay(10, cancellation);
        }
    }
}
