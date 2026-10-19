using Xunit;

namespace NativeChannels.Windows.Tests;

public sealed class LinuxRegistrationTests
{
    [Fact]
    public void PortableLinuxFactoryCanBeRegisteredWithoutLoadingNativeCode()
    {
        var registration = new NativeModuleRegistration("", "")
        {
            LinuxFactory = () =>
                new LinuxNativeModule("missing-linux-library", typeof(LinuxRegistrationTests).Assembly, []),
        };
        Assert.NotNull(registration.LinuxFactory);
        if (!OperatingSystem.IsLinux())
            Assert.Throws<PlatformNotSupportedException>(() => registration.LinuxFactory());
    }
}
