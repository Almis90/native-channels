using Microsoft.Maui.Controls;
using Microsoft.Maui.Storage;

namespace NativeChannels.Sample;

public sealed class MainPage(IChannelService channels) : ContentPage
{
    private readonly Label _status = new()
    {
        Text = "Checking native methods, messages, streams and views…",
        Margin = 24,
    };
    private bool _started;
    private readonly ViewVerification _views = new();
    private readonly Label _clicks = new() { Text = "Tap either native button to call C#.", Margin = 24 };

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_started)
        {
            return;
        }

        _started = true;
        Title = "NativeChannels";
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
        var reconnect = new Button { Text = "Recreate first native view", Margin = 24 };
        reconnect.Clicked += (_, _) => _views.First.Reconnect();
        _views.Clicked += text => _clicks.Text = text;
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 12,
                Children = { _status, _views.Panel, _clicks, reconnect },
            },
        };

        string report;
        try
        {
            var verification = new NativeVerification(channels);
            report = "PASS " + await verification.RunAsync() + "; " + await _views.RunAsync();
        }
        catch (Exception exception)
        {
            report = "FAIL " + exception;
        }

        _status.Text = report;
        Console.WriteLine("NATIVECHANNELS_SAMPLE " + report);
        var resultPath = Path.Combine(FileSystem.AppDataDirectory, "sample-result.txt");
#if WINDOWS
        resultPath = Environment.GetEnvironmentVariable("NATIVECHANNELS_SAMPLE_RESULT") ?? resultPath;
#endif
        File.WriteAllText(resultPath, report);
    }
}
