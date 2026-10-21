using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;

namespace AudioStreamer.Avalonia;

internal static class AvaloniaAudioVerification
{
#if UNO_SAMPLE
    public static async Task RunAsync(
        UnoChannelHost host,
        Microsoft.UI.Xaml.Window window,
        CancellationToken cancellation
    )
#else
    public static async Task RunAsync(AvaloniaChannelHost host, CancellationToken cancellation)
#endif
    {
        for (var index = 0; index < 3; index++)
        {
#if UNO_SAMPLE
            await using var owner = host.CreateSession(window);
#else
            await using var owner = host.CreateSession();
#endif
            var audio = owner.Services.GetRequiredService<IAudioStreamer>();
            // Deliberately leave playback owned by DI, including a blocked writer.
            var playback = await audio.OpenAsync(cancellation);
            var status = await playback.GetStatusAsync(cancellation);
            Check(status.BufferedFrames == 0 && status.PlayedFrames == 0, "fresh host session");
            await playback.WriteAsync(new byte[1920], cancellation);
            await playback.DrainAsync(cancellation);
            await playback.PauseAsync(cancellation);
            var blocked = playback.WriteAsync(new byte[1920], cancellation);
            await Task.Delay(50, cancellation);
            Check(!blocked.IsCompleted, "paused writer pending before host disposal");
            // Exercise the host's worker-thread disposal path and shared completion.
            var disposal = Task.Run(async () => await owner.DisposeAsync(), cancellation);
            await disposal.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            await owner.DisposeAsync();
            Check(owner.Stopping.IsCancellationRequested && playback.IsDisposed, "DI owns audio cleanup");
            try
            {
                await blocked.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
                throw new InvalidOperationException("Disposed audio accepted a pending write.");
            }
            catch (ObjectDisposedException) { }
            try
            {
                await audio.OpenAsync(cancellation);
                throw new InvalidOperationException("Disposed streamer opened playback.");
            }
            catch (ObjectDisposedException) { }
        }
    }

    internal static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("Failed: " + name);
    }
}
