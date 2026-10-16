using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Hosting;

namespace NativeChannels.PackageConsumer;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp() => MauiApp.CreateBuilder().UseMauiApp<ProbeApp>().Build();
}

public sealed class ProbeApp : Application
{
    protected override Window CreateWindow(IActivationState? activationState) => new(new ProbePage());
}

public sealed class ProbePage : ContentPage
{
    private readonly Label _status = new() { Text = "Running packaged native bridge probe…" };

    public ProbePage() => Content = _status;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        string result;
        try
        {
            result = await BridgeProbe.RunAsync().WaitAsync(TimeSpan.FromSeconds(30));
            if (result != "Kotlin:managed:hello" && result != "Swift:managed:hello")
            {
                throw new InvalidOperationException($"Unexpected native result: {result}");
            }

            result = "PASS " + result;
        }
        catch (Exception exception)
        {
            result = "FAIL " + exception;
        }
        _status.Text = result;
        Console.WriteLine("NATIVECHANNELS_PROBE " + result);
        File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "probe-result.txt"), result);
    }
}
