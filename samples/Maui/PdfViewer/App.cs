using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace PdfViewer.Sample;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new NavigationPage(page) { BarBackgroundColor = Color.FromArgb("12263A"), BarTextColor = Colors.White });
}
