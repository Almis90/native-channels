using Microsoft.Extensions.DependencyInjection;
using NativeChannels;
using Plugin.AudioStreamer;
using Xunit;

namespace AudioStreamer.Tests;

public sealed class AudioRegistrationTests
{
    [Fact]
    public void SharedAndHostOptionsPreserveFluentTypeAndRegisterOnce()
    {
        var shared = new ChannelRegistrationOptions();
        Assert.Same(shared, shared.AddAudioStreamer().AddAudioStreamer());
        var host = new HostOptions();
        HostOptions configured = host.AddAudioStreamer().AddAudioStreamer();
        Assert.Same(host, configured);
        Assert.Equal(Assert.Single(shared.Modules), Assert.Single(host.Modules));
    }

    [Fact]
    public async Task RepeatedRegistrationKeepsOneOwnedStreamer()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var channels = new ChannelService(new BinaryMessenger(pair.Managed));
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        var services = new ServiceCollection();
        services.AddSingleton<IChannelService>(channels);
        services.AddAudioStreamer().AddAudioStreamer();
        await using var provider = services.BuildServiceProvider();
        var audio = provider.GetRequiredService<IAudioStreamer>();
        Assert.Same(audio, provider.GetRequiredService<IAudioStreamer>());
        await provider.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => audio.OpenAsync());
        // The externally owned channel service is still usable after the facade is disposed.
        using var channel = channels.GetMethodChannel("still-owned-by-caller");
    }

    private sealed class HostOptions : ChannelRegistrationOptions;
}
