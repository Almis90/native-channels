using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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
            ["nativeMethod"] = "not-executed"
        };
        try
        {
            var transport = new NativeChannelTransport();
            transport.DisposeAsync().AsTask().GetAwaiter().GetResult();
            report["transportInitialization"] = "passed (no modules registered)";
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
        report["macOSNativeTypes"] = metadata.TypeDefinitions.Select(handle => metadata.GetTypeDefinition(handle))
            .Where(type => metadata.GetString(type.Name).Contains("NativeElement"))
            .Select(type => new
            {
                name = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name),
                visibility = (type.Attributes & TypeAttributes.VisibilityMask).ToString()
            }).ToArray();
        return JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    }
}
