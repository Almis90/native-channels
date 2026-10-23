namespace Plugin.AudioStreamer;

/// <summary>The example's fixed wire format: little-endian PCM16, mono, 48 kHz.</summary>
public static class AudioFormat
{
    public const int SampleRate = 48_000;
    public const int Channels = 1;
    public const int BytesPerFrame = 2;
    public const int MaxChunkFrames = 4_800;
    public const int BufferCapacityFrames = 24_000;
    internal const string ControlChannel = "plugin.maui.audio.control";
    internal const string DataChannel = "plugin.maui.audio.pcm";
}

public sealed record AudioPlaybackStatus(string State, int BufferedFrames, int CapacityFrames, long PlayedFrames)
{
    public double BufferUtilization => CapacityFrames == 0 ? 0 : (double)BufferedFrames / CapacityFrames;
    public TimeSpan BufferedDuration => TimeSpan.FromSeconds((double)BufferedFrames / AudioFormat.SampleRate);
}
