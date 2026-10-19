using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NativeChannels;

namespace UnoSamples;

internal sealed partial class SamplePage : Page, IAsyncDisposable
{
    private readonly Window _window;
    private readonly StackPanel _panel = new() { Spacing = 12, Margin = new Thickness(24) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ContentPresenter _surface = new();
    private readonly CancellationTokenSource _stopping = new();
    private UnoChannelHost? _host;
    private UnoChannelSession? _session;
    private Task? _dispose;
    private bool _loaded;
    private readonly SemaphoreSlim _actions = new(1);

    public SamplePage(Window window)
    {
        _window = window;
        Content = new ScrollViewer { Content = _panel };
        _panel.Children.Add(new TextBlock { Text = Title, FontSize = 28 });
        _panel.Children.Add(_status);
        _panel.Children.Add(_surface);
        Build();
        Loaded += async (_, _) =>
        {
            if (_loaded)
                return;
            _loaded = true;
            var result = Environment.GetEnvironmentVariable("NATIVECHANNELS_UNO_SAMPLE_RESULT");
            if (string.IsNullOrEmpty(result))
                return;
            string outcome;
            try
            {
                await VerifyAsync().WaitAsync(TimeSpan.FromSeconds(25));
                await DisposeAsync();
                outcome = System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        sample = Title,
                        checks = "passed",
                        limitation = Limitation,
                    }
                );
            }
            catch (Exception error)
            {
                outcome = System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        sample = Title,
                        checks = "failed",
                        error = error.ToString(),
                    }
                );
            }
            await File.WriteAllTextAsync(result, outcome);
            Application.Current.Exit();
        };
    }

    private UnoChannelSession Session(Action<IServiceCollection> configure)
    {
        if (_session is not null)
            return _session;
        _host = UnoChannelHost.Create(configure);
        return _session = _host.CreateSession(_window);
    }

    private void Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            await _actions.WaitAsync();
            try
            {
                if (!_stopping.IsCancellationRequested)
                    await action();
            }
            catch (OperationCanceledException)
            {
                _status.Text = "Stopped.";
            }
            catch (Exception error)
            {
                _status.Text = error.Message;
            }
            finally
            {
                _actions.Release();
                button.IsEnabled = !_stopping.IsCancellationRequested;
            }
        };
        _panel.Children.Add(button);
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new InvalidOperationException(description);
    }

    public ValueTask DisposeAsync() => new(_dispose ??= DisposeCoreAsync());

    private async Task DisposeCoreAsync()
    {
        IsEnabled = false;
        _stopping.Cancel();
        await _actions.WaitAsync();
        try
        {
            await StopAsync();
        }
        finally
        {
            try
            {
                if (_host is not null)
                    await _host.DisposeAsync();
            }
            finally
            {
                _actions.Release();
            }
        }
    }

    public async Task SuspendAsync()
    {
        await _actions.WaitAsync();
        try
        {
            await StopAsync();
        }
        finally
        {
            _actions.Release();
        }
    }
}
