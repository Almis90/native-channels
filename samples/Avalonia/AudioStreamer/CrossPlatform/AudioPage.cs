using AudioStreamer.Sample;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.Maui.AudioStreamer;

namespace ReferenceSamples;

internal sealed class AudioPage : SamplePage
{
    public override Task<string> VerifyAsync() => AudioVerification.RunAsync(_audio, Owner.Stopping);

    private readonly IAudioStreamer _audio;
    private readonly Slider _level = new()
    {
        Minimum = 0,
        Maximum = 0.25,
        Value = 0.16,
    };
    private readonly TextBlock _buffer = new();
    private CancellationTokenSource? _playback;
    private AudioStreamSession? _session;
    private Task _run = Task.CompletedTask;
    private Task _pause = Task.CompletedTask;
    private bool _paused;

    public AudioPage(AvaloniaChannelSession owner)
        : base(owner)
    {
        _audio = owner.Services.GetRequiredService<IAudioStreamer>();
        Status.Text = "Ready. Select Play to enable audio.";
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "Sunny Steps", FontSize = 30 },
                    new TextBlock
                    {
                        Text = "Original 16-second music · 48 kHz mono PCM16",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                    Status,
                    new TextBlock { Text = "Signal level (0–25%)" },
                    _level,
                    Button("Play", () => StartAsync(false)),
                    Button("Loop", () => StartAsync(true)),
                    Button("Pause / resume", TogglePauseAsync),
                    Button("Stop", SuspendAsync),
                    _buffer,
                    new TextBlock
                    {
                        Text =
                            "Playback stops when the app is hidden, minimized or closed. Browser playback requires a click and uses Web Audio.",
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    },
                },
            },
        };
    }

    private Task StartAsync(bool loop)
    {
        if (IsClosed || !_run.IsCompleted)
            return Task.CompletedTask;
        _playback?.Dispose();
        _playback = CancellationTokenSource.CreateLinkedTokenSource(Owner.Stopping);
        _pause = Task.CompletedTask;
        return _run = Attempt(() => PlayAsync(loop, _playback.Token));
    }

    private async Task PlayAsync(bool loop, CancellationToken token)
    {
        using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task monitor = Task.CompletedTask;
        try
        {
            _session = await _audio.OpenAsync(token);
            _paused = false;
            Status.Text = "Playing";
            var session = _session;
            monitor = MonitorAsync(session, monitorStop.Token);
            var music = new MusicGenerator();
            const int frames = 2400;
            var chunks = MusicGenerator.LoopFrames / frames;
            for (int i = 0; loop || i < chunks; i++)
            {
                token.ThrowIfCancellationRequested();
                await session.WriteAsync(music.Next(frames, _level.Value, !loop && i == chunks - 1), token);
            }
            await session.DrainAsync(token);
            Status.Text = "Completed";
        }
        finally
        {
            monitorStop.Cancel();
            try
            {
                await monitor;
            }
            catch (OperationCanceledException) { }
            try
            {
                await _pause;
            }
            finally
            {
                var session = _session;
                _session = null;
                if (session is not null)
                    await session.DisposeAsync();
            }
            _buffer.Text = "Buffer: 0 / 500 ms";
        }
    }

    private async Task MonitorAsync(AudioStreamSession session, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var state = await session.GetStatusAsync(token);
                _buffer.Text =
                    $"Buffer: {state.BufferedDuration.TotalMilliseconds:F0} / 500 ms · played {state.PlayedFrames / 48000.0:F1}s";
                await Task.Delay(100, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            Status.Text = error.Message;
            _playback?.Cancel();
        }
    }

    private Task TogglePauseAsync()
    {
        if (_session is null || !_pause.IsCompleted)
            return Task.CompletedTask;
        return _pause = ChangePauseAsync(_session);
    }

    private async Task ChangePauseAsync(AudioStreamSession session)
    {
        if (_paused)
            await session.ResumeAsync(_playback!.Token);
        else
            await session.PauseAsync(_playback!.Token);
        _paused = !_paused;
        Status.Text = _paused ? "Paused" : "Playing";
    }

    public override async Task SuspendAsync()
    {
        var playback = _playback;
        var pause = _pause;
        var run = _run;
        playback?.Cancel();
        try
        {
            await pause;
        }
        catch (OperationCanceledException) { }
        finally
        {
            await run;
        }
        playback?.Dispose();
        if (ReferenceEquals(_playback, playback))
            _playback = null;
        Status.Text = "Stopped";
    }
}
