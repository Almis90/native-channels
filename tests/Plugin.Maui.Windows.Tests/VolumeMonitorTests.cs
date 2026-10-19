using System.Runtime.InteropServices;
using NativeChannels;
using Plugin.VolumeMonitor;
using Xunit;

namespace Plugin.Maui.Windows.Tests;

public sealed class VolumeMonitorTests
{
    [Fact]
    public void RegistrationIsIdempotentAndCreatesSeparateModules()
    {
        var options = new NativeChannelsOptions().AddVolumeMonitor().AddVolumeMonitor();
        var registration = Assert.Single(options.Modules);
        using var first = registration.WindowsFactory!();
        using var second = registration.WindowsFactory();
        Assert.NotSame(first, second);
    }

    [Fact, Trait("Category", "AudioHardware")]
    public async Task IndependentListenersCancelAndRestart()
    {
        await using var channels = CreateChannels();
        var monitor = new VolumeMonitor.VolumeMonitor(channels);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.StartsWith(
            "PASS",
            await global::VolumeMonitor.Sample.VolumeVerification.RunAsync(monitor, deadline.Token)
        );
    }

    [Fact, Trait("Category", "AudioHardware")]
    public async Task RealEndpointChangesReachBothListenersAndRestoreOriginalVolume()
    {
        // Explicitly excluded by the build-only CI filter. Local execution changes volume briefly.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var channels = CreateChannels();
        var monitor = new VolumeMonitor.VolumeMonitor(channels);
        await using var first = monitor.ListenAsync(deadline.Token).GetAsyncEnumerator();
        await using var second = monitor.ListenAsync(deadline.Token).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());
        var enumerator = (CoreAudio.DeviceEnumerator)
            Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
        CoreAudio.Device? device = null;
        CoreAudio.EndpointVolume? volume = null;
        float? original = null;
        var context = Guid.NewGuid();
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
            var iid = typeof(CoreAudio.EndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, 0, out var endpoint));
            volume = (CoreAudio.EndpointVolume)endpoint;
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var value));
            original = value;
            var changed = value >= 0.02f ? value - 0.02f : value + 0.02f;
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(changed, ref context));
            await ExpectLevel(first, changed);
            await ExpectLevel(second, changed);
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(value, ref context));
            await ExpectLevel(first, value);
            await ExpectLevel(second, value);
        }
        finally
        {
            if (volume is not null)
            {
                try
                {
                    if (original is { } value)
                        Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(value, ref context));
                }
                finally
                {
                    Marshal.ReleaseComObject(volume);
                }
            }
            if (device is not null)
                Marshal.ReleaseComObject(device);
            Marshal.ReleaseComObject(enumerator);
        }
    }

    private static async Task ExpectLevel(IAsyncEnumerator<double> listener, float expected)
    {
        do
        {
            Assert.True(await listener.MoveNextAsync());
        } while (Math.Abs(listener.Current - expected) > 0.001);
    }

    private static ChannelService CreateChannels() =>
        new(new BinaryMessenger(new NativeChannelTransport(new NativeChannelsOptions().AddVolumeMonitor().Modules)));
}
