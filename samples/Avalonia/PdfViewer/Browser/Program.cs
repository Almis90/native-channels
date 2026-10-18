using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using NativeChannels;

namespace ReferenceSamples;

internal static partial class Program
{
    private static async Task Main(string[] args)
    {
        await Plugin.Avalonia.PdfViewer.PdfBrowser.InitializeAsync();
        await AppBuilder
            .Configure<SampleApplication>()
            .UseNativeChannels(SampleFeature.Configure)
            .StartBrowserAppAsync("out");
    }

    [JSExport]
    public static Task<string> VerifyAsync() => ((SampleApplication)Avalonia.Application.Current!).VerifyAsync();

    [JSExport]
    public static Task SuspendAsync() => ((SampleApplication)Avalonia.Application.Current!).SuspendAsync();

    [JSExport]
    public static Task CloseAsync() => ((SampleApplication)Avalonia.Application.Current!).CloseAsync();
}
