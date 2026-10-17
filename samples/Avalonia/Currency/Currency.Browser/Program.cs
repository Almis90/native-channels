using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using Currency;
using Currency.Browser;
using NativeChannels;

internal sealed partial class Program
{
    private static string _result = "pending";

    private static Task Main(string[] args)
    {
        App.ResultReported = result => _result = result;
        return BuildAvaloniaApp().StartBrowserAppAsync("out");
    }

    [JSExport]
    public static string GetVerificationResult() => _result;

    [JSExport]
    public static Task CloseAsync() => ((App)Application.Current!).CloseAsync();

    [JSExport]
    public static Task<string> VerifyTransportAsync() => TransportVerification.RunAsync();

    [JSExport]
    public static string[] GetDisplayedText() =>
        ((ISingleViewApplicationLifetime)Application.Current!.ApplicationLifetime!)
            .MainView!.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(block => block.Text ?? "")
            .ToArray();

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().WithInterFont().UseNativeChannels(NativeCurrency.ConfigureServices);
}
