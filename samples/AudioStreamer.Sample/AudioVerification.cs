using Plugin.Maui.AudioStreamer;

namespace AudioStreamer.Sample;

internal static class AudioVerification
{
    public static async Task<string> RunAsync(IAudioStreamer audio, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using (var session = await audio.OpenAsync(deadline.Token))
        {
            // Silent PCM exercises native playback clocks without autoplaying music.
            for (var index = 0; index < 35; index++)
                await session.WriteAsync(new byte[1920], deadline.Token);
            var status = await session.GetStatusAsync(deadline.Token);
            Check(
                status.BufferedFrames <= AudioFormat.BufferCapacityFrames && status.PlayedFrames > 0,
                "bounded native playback progress"
            );
            await session.PauseAsync(deadline.Token);
            var paused = await session.GetStatusAsync(deadline.Token);
            Check(paused.State == "paused", "pause");
            using (var cancellation = new CancellationTokenSource(100))
            {
                try
                {
                    await session.WriteAsync(new byte[1920], cancellation.Token);
                    throw new InvalidOperationException("Paused write did not wait");
                }
                catch (OperationCanceledException) { }
            }
            await session.ResumeAsync(deadline.Token);
            await session.DrainAsync(deadline.Token);
            status = await session.GetStatusAsync(deadline.Token);
            Check(status.BufferedFrames == 0 && status.PlayedFrames == 35 * 960L, "drained PCM frame count");
        }
        await using (var restarted = await audio.OpenAsync(deadline.Token))
        {
            var status = await restarted.GetStatusAsync(deadline.Token);
            Check(status.BufferedFrames == 0 && status.PlayedFrames == 0, "stop and clean restart");
            await restarted.WriteAsync(new byte[1920], deadline.Token);
            await restarted.DrainAsync(deadline.Token);
        }
        return "PASS native PCM playback, bounded buffering, pause/resume, cancellation, drain and clean restart";
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("Failed: " + name);
    }
}
