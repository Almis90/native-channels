using NativeChannels;
using Plugin.AudioStreamer;
using Xunit;

namespace Plugin.Maui.Windows.Tests;

public sealed class AudioStreamerTests
{
    [Fact]
    public void RegistrationIsIdempotentAndCreatesSeparateModules()
    {
        var options = new ChannelRegistrationOptions().AddAudioStreamer().AddAudioStreamer();
        var registration = Assert.Single(options.Modules);
        using var first = registration.WindowsFactory!();
        using var second = registration.WindowsFactory();
        Assert.NotSame(first, second);
    }

    [Fact, Trait("Category", "AudioHardware")]
    public async Task SilentNativePlaybackPassesTheConsumerChecks()
    {
        await using var channels = CreateChannels();
        await using var audio = new AudioStreamer.AudioStreamer(channels);
        Assert.StartsWith("PASS", await global::AudioStreamer.Sample.AudioVerification.RunAsync(audio));
    }

    [Fact]
    public async Task MalformedSessionProducesStructuredWindowsError()
    {
        await using var channels = CreateChannels();
        using var control = channels.GetMethodChannel(AudioFormat.ControlChannel);
        foreach (
            var arguments in new object?[]
            {
                null,
                42,
                new Dictionary<string, object?> { ["session"] = "invalid" },
                new Dictionary<string, object?> { ["session"] = new byte[15] },
            }
        )
        {
            var error = await Assert.ThrowsAsync<ChannelException>(() => control.InvokeAsync("open", arguments));
            Assert.Equal("invalid_arguments", error.Code);
            Assert.Equal("windows", error.Platform);
        }
    }

    [Fact, Trait("Category", "AudioHardware")]
    public async Task LeaseCancellationRejectsOldPcmAndAllowsImmediateReopen()
    {
        await using var channels = CreateChannels();
        using var control = channels.GetMethodChannel(AudioFormat.ControlChannel);
        using var data = channels.GetBasicMessageChannel(
            AudioFormat.DataChannel,
            new() { Codec = BinaryMessageCodec.Instance }
        );
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid().ToByteArray();
        var arguments = new Dictionary<string, object?> { ["session"] = id };
        var lease = control.InvokeAsync(
            "open",
            arguments,
            cancellation.Token,
            new() { Timeout = Timeout.InfiniteTimeSpan }
        );
        await WaitForPlaying(control, arguments);
        var busy = await Assert.ThrowsAsync<ChannelException>(() => control.InvokeAsync("open", arguments));
        Assert.Equal("audio_busy", busy.Code);
        var stale = new Dictionary<string, object?> { ["session"] = Guid.NewGuid().ToByteArray() };
        var mismatch = await Assert.ThrowsAsync<ChannelException>(() => control.InvokeAsync("stop", stale));
        Assert.Equal("audio_session_mismatch", mismatch.Code);
        Assert.Equal(new byte[] { 4 }, await data.SendAsync<byte[]>(new byte[17]));
        Assert.Equal(new byte[] { 3 }, await data.SendAsync<byte[]>(new byte[18]));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease);
        // Transport cancellation is asynchronous; wait for the native lease callback to finish.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (
            (await control.InvokeAsync<Dictionary<string, object?>>("status", arguments, timeout.Token))!["state"]
                as string
            != "stopped"
        )
            await Task.Delay(10, timeout.Token);
        var pcm = id.Concat(new byte[1920]).ToArray();
        Assert.Equal(new byte[] { 3 }, await data.SendAsync<byte[]>(pcm));
        await using var audio = new AudioStreamer.AudioStreamer(channels);
        await using var reopened = await audio.OpenAsync(timeout.Token);
        await reopened.WriteAsync(new byte[1920], timeout.Token);
        await reopened.DrainAsync(timeout.Token);
        Assert.Equal(960, (await reopened.GetStatusAsync(timeout.Token)).PlayedFrames);
    }

    [Fact, Trait("Category", "AudioHardware")]
    public async Task DisposingTheBridgeClosesAnOutstandingNativeLease()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var managed = new ChannelService(new BinaryMessenger(pair.Managed));
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        using var module = new WindowsAudioStreamerModule();
        module.Register(native);
        using var control = managed.GetMethodChannel(AudioFormat.ControlChannel);
        var arguments = new Dictionary<string, object?> { ["session"] = Guid.NewGuid().ToByteArray() };
        var lease = control.InvokeAsync("open", arguments, options: new() { Timeout = Timeout.InfiniteTimeSpan });
        await WaitForPlaying(control, arguments);
        module.Dispose();
        // Unregistering the handler may cancel the reply before the completed lease is delivered.
        try
        {
            await lease.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException) { }
        module.Dispose();
        await Assert.ThrowsAsync<ChannelNotFoundException>(() => control.InvokeAsync("status", arguments));
    }

    private static ChannelService CreateChannels() =>
        new(
            new BinaryMessenger(new NativeChannelTransport(new ChannelRegistrationOptions().AddAudioStreamer().Modules))
        );

    private static async Task WaitForPlaying(MethodChannel control, object arguments)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (
            (await control.InvokeAsync<Dictionary<string, object?>>("status", arguments, timeout.Token))!["state"]
                as string
            != "playing"
        )
            await Task.Delay(10, timeout.Token);
    }
}
