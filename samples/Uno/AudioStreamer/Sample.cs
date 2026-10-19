using NativeChannels;
using Plugin.AudioStreamer;

namespace UnoSamples;

internal sealed partial class SamplePage
{
    private const string Title = "AudioStreamer";
    private const string Limitation =
        "Windows desktop silent transport verified; listening and other platforms pending.";
    private AudioStreamSession? _playback;
    private CancellationTokenSource? _playing;
    private Task _producer = Task.CompletedTask;
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
        Button(
            "Play",
            async () =>
            {
                await StopAsync();
                _playback = await Audio.OpenAsync(_stopping.Token);
                _playing = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                _producer = ProduceAsync(_playback, _playing.Token);
            }
        );
        Button(
            "Pause",
            async () =>
            {
                if (_playback is not null)
                {
                    await _playback.PauseAsync();
                    _status.Text = "Paused.";
                }
            }
        );
        Button(
            "Resume",
            async () =>
            {
                if (_playback is not null)
                {
                    await _playback.ResumeAsync();
                    _status.Text = "Playing.";
                }
            }
        );
        Button("Stop", StopAsync);
    }

    private async Task ProduceAsync(AudioStreamSession playback, CancellationToken token)
    {
        try
        {
            var music = new global::AudioStreamer.Sample.MusicGenerator();
            _status.Text = "Playing.";
            for (var frames = 0; frames < global::AudioStreamer.Sample.MusicGenerator.LoopFrames; frames += 2400)
                await playback.WriteAsync(music.Next(2400, 0.12), token);
            await playback.DrainAsync(token);
            _status.Text = "Finished.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error)
        {
            _status.Text = error.Message;
        }
        finally
        {
            await playback.DisposeAsync();
        }
    }

    private async Task VerifyAsync()
    {
        await using var playback = await Audio.OpenAsync(_stopping.Token);
        await playback.WriteAsync(new byte[960], _stopping.Token);
        await playback.PauseAsync();
        Require((await playback.GetStatusAsync()).State == "paused", "Pause");
        await playback.ResumeAsync();
        Require((await playback.GetStatusAsync()).State == "playing", "Resume");
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
