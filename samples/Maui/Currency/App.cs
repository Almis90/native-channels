using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Currency.Sample;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var navigation = new NavigationPage(page)
        {
            BarBackgroundColor = Color.FromArgb("2C3E50"),
            BarTextColor = Colors.White,
        };
        return new Window(navigation);
    }
}
