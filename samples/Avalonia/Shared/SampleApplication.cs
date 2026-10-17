using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using NativeChannels;

namespace ReferenceSamples;

public sealed class SampleApplication : global::Avalonia.Application
{
    private SamplePage? _page;
    private Task? _closing;
    private bool _closed;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        EnsureView();
        base.OnFrameworkInitializationCompleted();
    }

    public void EnsureView()
    {
        if (_page is { IsClosed: false })
            return;
        _page = SampleFeature.Create(this.GetNativeChannels().CreateSession());
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
            single.MainView = _page;
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Window
            {
                Title = SampleFeature.Title,
                Width = 720,
                Height = 780,
                MinWidth = 360,
                Content = _page,
            };
            desktop.MainWindow = window;
            window.PropertyChanged += async (_, args) =>
            {
                if (args.Property == Window.WindowStateProperty && window.WindowState == WindowState.Minimized)
                    await SuspendAsync();
            };
            window.Closing += (_, args) =>
            {
                if (!_closed)
                {
                    args.Cancel = true;
                    _ = CloseAsync();
                }
            };
            desktop.ShutdownRequested += (_, args) =>
            {
                if (!_closed)
                {
                    args.Cancel = true;
                    _ = CloseAsync();
                }
            };
        }
    }

    public Task SuspendAsync() => _page?.SuspendForLifecycleAsync() ?? Task.CompletedTask;

    public async Task CloseViewAsync()
    {
        if (_page is not null)
            await _page.DisposeAsync();
    }

    public Task CloseAsync()
    {
        if (_closing is not null)
            return _closing;
        _closing = CloseCoreAsync();
        _ = _closing.ContinueWith(
            task => System.Diagnostics.Trace.TraceError(task.Exception!.ToString()),
            TaskContinuationOptions.OnlyOnFaulted
        );
        return _closing;
    }

    private async Task CloseCoreAsync()
    {
        try
        {
            await CloseViewAsync();
        }
        finally
        {
            try
            {
                await this.GetNativeChannels().DisposeAsync();
            }
            finally
            {
                _closed = true;
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            }
        }
    }
}

public abstract class SamplePage(AvaloniaChannelSession owner) : UserControl, IAsyncDisposable
{
    protected AvaloniaChannelSession Owner { get; } = owner;
    protected TextBlock Status { get; } = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    public bool IsClosed { get; private set; }
    private Task? _closing;
    public abstract Task SuspendAsync();

    internal Task SuspendForLifecycleAsync() => Attempt(SuspendAsync);

    protected async Task Attempt(Func<Task> operation)
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (IsClosed) { }
        catch (Exception error)
        {
            Status.Text = error.Message;
        }
    }

    protected Button Button(string title, Func<Task> action)
    {
        var button = new Button { Content = title };
        button.Click += async (_, _) => await Attempt(action);
        return button;
    }

    public ValueTask DisposeAsync() => new(_closing ??= CloseCoreAsync());

    private async Task CloseCoreAsync()
    {
        IsClosed = true;
        try
        {
            await SuspendAsync();
        }
        finally
        {
            await Owner.DisposeAsync();
        }
    }
}
