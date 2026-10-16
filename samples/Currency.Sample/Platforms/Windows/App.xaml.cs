using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace Currency.Sample.WinUI;

public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
