using Uno.UI.Hosting;

namespace UnoSamples;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder
            .Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            // Uno 6.7.135 can deadlock joining its OpenGL render thread on close.
            // Keep normal shutdown reliable using the public software backend.
            .UseWin32(options => options.RenderingBackend(Win32RenderingBackend.Software))
            .Build();

        host.Run();
    }
}
