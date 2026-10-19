using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using NativeChannels;

namespace UnoFeasibility;

// Desktop feasibility only: inspect the exact restored binary, without loading macOS code on Windows.
internal static class DependencyProbe
{
    public static string Run()
    {
        var report = new Dictionary<string, object?>
        {
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["framework"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["nativeChannelsAssembly"] = typeof(NativeChannelTransport).Assembly.Location,
            ["nativeMethod"] = "not-executed",
        };
        try
        {
            ProbeMethodAsync(report).GetAwaiter().GetResult();
            if (OperatingSystem.IsMacOS())
            {
                DesktopContractProbe.RunAsync().WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                report["desktopContract"] = "passed";
            }
        }
        catch (Exception error)
        {
            report["transportInitialization"] = "blocked";
            report["transportError"] = error.GetType().Name + ": " + error.Message;
        }
        var path = Path.Combine(AppContext.BaseDirectory, "Uno.UI.Runtime.Skia.MacOS.dll");
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        report["macOSAssembly"] = path;
        report["macOSAssemblyVersion"] = metadata.GetAssemblyDefinition().Version.ToString();
        report["macOSNativeTypes"] = metadata
            .TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Where(type => metadata.GetString(type.Name).Contains("NativeElement"))
            .Select(type => new
            {
                name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name),
                visibility = (type.Attributes & TypeAttributes.VisibilityMask).ToString(),
            })
            .ToArray();
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }

    private static async Task ProbeMethodAsync(Dictionary<string, object?> report)
    {
        var registration = OperatingSystem.IsMacOS()
            ? new NativeModuleRegistration("", "MCCurrencyModule", "MauiCurrencyNative")
            : new NativeModuleRegistration("", "")
            {
                WindowsFactory = () => new NativeProbeModule(),
                LinuxFactory = () => new NativeProbeModule(),
            };
        await using var channels = new ChannelService(new BinaryMessenger(new NativeChannelTransport([registration])));
        report["transportInitialization"] = "passed";
        using var channel = channels.GetMethodChannel(
            OperatingSystem.IsMacOS() ? "plugin.maui.currency" : "desktop.probe"
        );
        var result = await channel.InvokeAsync<object>(OperatingSystem.IsMacOS() ? "getSymbol" : "native", "EUR");
        report["nativeMethod"] = "passed";
        report["nativeResult"] = result;
    }

    private sealed class NativeProbeModule : IWindowsChannelModule, ILinuxChannelModule
    {
        public void Register(IChannelService channels) =>
            channels
                .GetMethodChannel("desktop.probe")
                .SetMethodCallHandler(
                    (_, _) =>
                        ValueTask.FromResult<object?>(
                            OperatingSystem.IsWindows() ? (long)GetDoubleClickTime() : GetProcessId()
                        )
                );

        public void Dispose() { }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("libc", EntryPoint = "getpid")]
    private static extern int GetProcessId();
}
