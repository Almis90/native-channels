using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using NativeChannels;

namespace AudioStreamer.Avalonia;

public sealed class App : global::Avalonia.Application
{
    internal static bool VerifyAndExit { get; set; }
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _window;
    private Task? _shutdown;
    private bool _closed;
    private int _exitCode;
    private string _result = "CLOSED before verification completed";

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            var host = this.GetNativeChannels();
            _window = new MainWindow(host.CreateSession());
            desktop.MainWindow = _window;
            _window.Opened += async (_, _) =>
            {
                try
                {
                    _result = await _window.InitializeAsync(host, VerifyAndExit);
                }
                catch (OperationCanceledException) when (_shutdown is not null) { }
                catch (Exception error)
                {
                    _result = "FAIL " + error;
                    _exitCode = 1;
                    _window.ShowError(error);
                }
                Console.WriteLine("AUDIO_AVALONIA " + _result);
                if (VerifyAndExit)
                    Dispatcher.UIThread.Post(() => _window.Close());
            };
            _window.Closing += (_, args) =>
            {
                if (_closed)
                    return;
                args.Cancel = true;
                _ = CloseAsync();
            };
            desktop.ShutdownRequested += (_, args) =>
            {
                if (_closed)
                    return;
                args.Cancel = true;
                _ = CloseAsync();
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    private Task CloseAsync() => _shutdown ??= CloseCoreAsync();

    private async Task CloseCoreAsync()
    {
        try
        {
            try
            {
                if (_window is not null)
                    await _window.CloseAsync();
            }
            finally
            {
                await this.GetNativeChannels().DisposeAsync();
            }
            Console.WriteLine("AUDIO_AVALONIA_CLOSED");
        }
        catch (Exception error)
        {
            _exitCode = 1;
            _result = "FAIL shutdown: " + error;
            Console.WriteLine("AUDIO_AVALONIA " + _result);
        }
        try
        {
            if (Environment.GetEnvironmentVariable("AUDIO_AVALONIA_RESULT") is { Length: > 0 } destination)
            {
                File.WriteAllText(destination + ".tmp", _result);
                File.Move(destination + ".tmp", destination, overwrite: true);
            }
        }
        catch (Exception error)
        {
            _exitCode = 1;
            Console.WriteLine("AUDIO_AVALONIA FAIL result file: " + error);
        }
        _closed = true;
        _desktop?.Shutdown(_exitCode);
    }
}
