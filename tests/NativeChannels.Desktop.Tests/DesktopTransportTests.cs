using Xunit;

namespace NativeChannels.Desktop.Tests;

public sealed class DesktopTransportTests
{
    [Fact]
    public async Task RuntimeSelectsOnlyTheCurrentOperatingSystemFactory()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return;
        var windows = 0;
        var linux = 0;
        var module = new Module();
        var registration = new NativeModuleRegistration("unused", "unused")
        {
            WindowsFactory = () =>
            {
                windows++;
                return module;
            },
            LinuxFactory = () =>
            {
                linux++;
                return module;
            },
        };
        await using (var channels = new ChannelService(new BinaryMessenger(new NativeChannelTransport([registration]))))
        {
            using var channel = channels.GetMethodChannel("desktop");
            Assert.Equal(42, await channel.InvokeAsync<int>("echo", 42));
        }
        Assert.Equal(OperatingSystem.IsWindows() ? 1 : 0, windows);
        Assert.Equal(OperatingSystem.IsLinux() ? 1 : 0, linux);
        Assert.True(module.Disposed);
    }

    private sealed class Module : IWindowsChannelModule, ILinuxChannelModule
    {
        public bool Disposed { get; private set; }

        public void Register(IChannelService channels) =>
            channels
                .GetMethodChannel("desktop")
                .SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));

        public void Dispose() => Disposed = true;
    }
}
