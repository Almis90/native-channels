namespace UnoFeasibility;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += (_, _) => Release();
    }

    private nint _button;

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        var hostResult = Environment.GetEnvironmentVariable("NATIVECHANNELS_UNO_HOST_PROBE_RESULT");
        if (!string.IsNullOrEmpty(hostResult))
        {
            try
            {
                await HostProbe
                    .RunAsync(((App)Application.Current).MainWindow!, NativeSurface)
                    .WaitAsync(TimeSpan.FromSeconds(20));
                File.WriteAllText(hostResult, "{\"host\":\"passed\"}");
            }
            catch (Exception error)
            {
                File.WriteAllText(
                    hostResult,
                    System.Text.Json.JsonSerializer.Serialize(new { host = "failed", error = error.ToString() })
                );
            }
            Application.Current.Exit();
            return;
        }
        if (!OperatingSystem.IsWindows())
        {
            Status.Text = "Native view probe not implemented on this target. See the feasibility report.";
            return;
        }
        try
        {
            // The Page can load before its window has completed activation.
            await Task.Delay(250);
            _button = Win32Probe.CreateButton();
            NativeSurface.Content = new Uno.UI.NativeElementHosting.Win32NativeWindow(_button);
            NativeSurface.UpdateLayout();
            await Task.Delay(1000);
            if (_button == 0)
                return;
            var first = Win32Probe.IsAttached(_button);
            NativeSurface.Content = null;
            await Task.Delay(250);
            Release();
            _button = Win32Probe.CreateButton();
            NativeSurface.Content = new Uno.UI.NativeElementHosting.Win32NativeWindow(_button);
            NativeSurface.UpdateLayout();
            await Task.Delay(1000);
            var second = _button != 0 && Win32Probe.IsAttached(_button);
            Status.Text = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    nativeView = first && second ? "attached-and-recreated" : "failed",
                    firstAttached = first,
                    secondAttached = second,
                    nativeMethod = "not-executed; see dependency probe",
                    inputFocusAndVisibility = "not-verified (hidden automated launch)",
                }
            );
        }
        catch (Exception error)
        {
            Status.Text = "FAILED: " + error;
            Release();
        }
        var resultPath = Environment.GetEnvironmentVariable("NATIVECHANNELS_UNO_PROBE_RESULT");
        if (!string.IsNullOrEmpty(resultPath))
        {
            File.WriteAllText(resultPath, Status.Text);
            Release();
            Application.Current.Exit();
        }
    }

    private void Release()
    {
        NativeSurface.Content = null;
        if (_button == 0)
            return;
        Win32Probe.DestroyWindow(_button);
        _button = 0;
    }
}
