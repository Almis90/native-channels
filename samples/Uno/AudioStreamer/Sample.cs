using NativeChannels;
using Plugin.AudioStreamer;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "AudioStreamer";
    private const string Limitation =
        "Windows, macOS ARM64 desktop, iOS ARM64 simulator and Android x64 emulator silent transport verified; listening and other targets pending.";
    private AudioStreamSession? _playback;
    private CancellationTokenSource? _playing;
    private Task _producer = Task.CompletedTask;
    private Task _suspension = Task.CompletedTask;
    private Exception? _playbackError;
    private IAudioStreamer Audio =>
        Session(services =>
        {
            services.AddNativeChannels(options => options.AddAudioStreamer());
            services.AddAudioStreamer();
        })
            .Services.GetRequiredService<IAudioStreamer>();

    private void Build()
    {
        _status.Text =
            "Play the shared original 16-second music loop at a low level. Playback starts only when requested.";
        Button("Play", () => StartAsync());
        Button("Pause", PauseAsync);
        Button("Resume", ResumeAsync);
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
    }

    private async Task StartAsync(bool silent = false)
    {
        await StopAsync();
        _playbackError = null;
        _playback = await Audio.OpenAsync(_stopping.Token);
        _playing = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        _producer = ProduceAsync(_playback, silent, _playing.Token);
    }

    private async Task PauseAsync()
    {
        if (_playback is not null)
        {
            await _playback.PauseAsync(_stopping.Token);
            _status.Text = "Paused.";
        }
    }

    private async Task ResumeAsync()
    {
        if (_playback is not null)
        {
            await _playback.ResumeAsync(_stopping.Token);
            _status.Text = "Playing.";
        }
    }

    private async Task ProduceAsync(AudioStreamSession playback, bool silent, CancellationToken token)
    {
        try
        {
            var music = new global::AudioStreamer.Sample.MusicGenerator();
            _status.Text = "Playing.";
            for (var frames = 0; frames < global::AudioStreamer.Sample.MusicGenerator.LoopFrames; frames += 2400)
                await playback.WriteAsync(music.Next(2400, silent ? 0 : 0.12), token);
            await playback.DrainAsync(token);
            _status.Text = "Finished.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _playbackError = error;
            _status.Text = error.Message;
        }
        finally
        {
            await playback.DisposeAsync();
        }
    }

    private async Task VerifyAsync()
    {
        Console.WriteLine(await global::AudioStreamer.Sample.AudioVerification.RunAsync(Audio, _stopping.Token));
        await global::AudioStreamer.Avalonia.AvaloniaAudioVerification.RunAsync(_host!, _window, _stopping.Token);
        await VerifyControlsAsync();
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
    }
}
