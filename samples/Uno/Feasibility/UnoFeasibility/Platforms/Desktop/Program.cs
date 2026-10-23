using Uno.UI.Hosting;

namespace UnoFeasibility;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--probe-dependencies"))
        {
            Console.WriteLine(DependencyProbe.Run());
            return;
        }
        App.InitializeLogging();

        var host = UnoPlatformHostBuilder.Create()
            .App(() => new App())
            .UseX11()
            .UseLinuxFrameBuffer()
            .UseMacOS()
            .UseWin32()
            .Build();

        host.Run();
    }
}
