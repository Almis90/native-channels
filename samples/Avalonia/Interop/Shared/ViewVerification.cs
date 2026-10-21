using MainThread = ChannelExplorer.SampleThread;
#if UNO_SAMPLE
using Microsoft.UI.Xaml.Controls;
using AvaloniaChannelView = NativeChannels.UnoChannelView;
#else
using Avalonia;
using Avalonia.Controls;
#endif

namespace ChannelExplorer;

internal sealed class ViewVerification
{
    public AvaloniaChannelView First { get; } = Create("First native button");
    public AvaloniaChannelView Second { get; } = Create("Second native button");
    public StackPanel Panel { get; } = new() { Spacing = 12 };
    private readonly TaskCompletionSource _firstReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _secondReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public event Action<string>? Clicked;

    public ViewVerification()
    {
        First.Connected += (_, _) => Bind(First, _firstReady);
        Second.Connected += (_, _) => Bind(Second, _secondReady);
        Panel.Children.Add(First);
        Panel.Children.Add(Second);
    }

    private static AvaloniaChannelView Create(string title) =>
#if UNO_SAMPLE
#if UNO_LINUX_SAMPLE
        new(id => new NativeChannels.UnoLinuxNativeView(
            "nc_sample",
            typeof(SamplePlatform).Assembly,
            id,
            new Dictionary<string, object?> { ["title"] = title }
        ))
        {
            Height = 72,
        };
#elif IOS || ANDROID
        new(id => new MobileButtonView(id, title)) { Height = 72 };
#else
        new(id => new WindowsButtonView(id, title)) { Height = 72 };
#endif
#else
        new((parent, id) => SampleButtonFactory.Create(parent, id, title)) { Height = 72 };
#endif

    private static async Task Reconnect(AvaloniaChannelView view)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Connected(object? sender, EventArgs args) => ready.TrySetResult();
        view.Connected += Connected;
        try
        {
            view.Reconnect();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            view.Connected -= Connected;
        }
    }

    public Task ReconnectFirstAsync() => Reconnect(First);

    public async Task ResetAsync()
    {
        await Reconnect(First);
        await Reconnect(Second);
    }

    public async Task DisconnectAsync()
    {
        await First.DisconnectAsync();
        await Second.DisconnectAsync();
    }

    private void Bind(AvaloniaChannelView view, TaskCompletionSource ready)
    {
        view.Channel!.SetMethodCallHandler(
            (call, _) =>
            {
                if (!MainThread.IsMainThread)
                    throw new InvalidOperationException("View callback was not on the UI thread.");
                if (call.Method != "clicked")
                    throw new MethodNotImplementedException();
                if (call.GetArgument<string>("instanceId") != view.InstanceId)
                    throw new InvalidOperationException("Callback came from a stale native instance.");
                Clicked?.Invoke($"Native click: {call.GetArgument<int>("count")} · {view.InstanceId}");
                return ValueTask.FromResult<object?>("managed acknowledgement");
            }
        );
        ready.TrySetResult();
    }

    public async Task<string> RunAsync()
    {
        await Task.WhenAll(_firstReady.Task, _secondReady.Task).WaitAsync(TimeSpan.FromSeconds(10));
        CheckLayout(First);
        CheckLayout(Second);
        var first = First.Channel!;
        var second = Second.Channel!;
        Check(first.Name != second.Name && First.InstanceId != Second.InstanceId, "independent IDs");
        Check((await State(First))["title"] as string == "First native button", "creation arguments");
        Check(await first.InvokeAsync<string>("simulateTap") == "managed acknowledgement", "native click callback");
        Check(Convert.ToInt32((await State(First))["count"]) == 1, "first instance mutation");
        Check(Convert.ToInt32((await State(Second))["count"]) == 0, "second instance isolation");
        var oldId = First.InstanceId;
        var pending = first.InvokeAsync("wait");
        await Reconnect(First);
        await ExpectDisposed(pending);
        await ExpectDisposed(first.InvokeAsync("getState"));
        Check(First.InstanceId != oldId, "fresh ID after reconnect");
        Check(Convert.ToInt32((await State(First))["count"]) == 0, "fresh native instance");
        Check(
            await second.InvokeAsync<string>("simulateTap") == "managed acknowledgement",
            "other view remains usable"
        );
        var disconnected = First.Channel!;
        await First.DisconnectAsync();
        Check(First.Channel is null && First.InstanceId is null, "cleared detached state");
        await ExpectDisposed(disconnected.InvokeAsync("getState"));
        await Reconnect(First);
        Check((await State(First))["uiThread"] is true, "native UI thread");
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Disconnected(object? sender, EventArgs args) => unloaded.TrySetResult();
        void Connected(object? sender, EventArgs args) => reloaded.TrySetResult();
        var beforeUnload = First.Channel!;
        First.Disconnected += Disconnected;
        First.Connected += Connected;
        try
        {
            Panel.Children.Remove(First);
            await unloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await ExpectDisposed(beforeUnload.InvokeAsync("getState"));
            Panel.Children.Insert(0, First);
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Check(Convert.ToInt32((await State(First))["count"]) == 0, "reload recreates native instance");
        }
        finally
        {
            First.Disconnected -= Disconnected;
            First.Connected -= Connected;
        }
        // The single-threaded browser runtime schedules Task.Run on the same UI
        // thread. Only threaded targets can exercise off-thread rejection.
        if (!OperatingSystem.IsBrowser())
            await Task.Run(() =>
            {
                try
                {
                    First.Reconnect();
                }
                catch (InvalidOperationException)
                {
                    return;
                }
                throw new InvalidOperationException("Background reconnect was accepted.");
            });
        await VerifyRepeatedReconnectsAsync();
        return "native views: instances, creation arguments, callbacks, reconnect, unload/reload, stale channels, cancellation and UI dispatch; view stress: 32 reconnects"
            + (
                OperatingSystem.IsBrowser()
                    ? "; background-thread rejection not applicable to this single-threaded browser runtime"
                    : ""
            );
    }

    private async Task VerifyRepeatedReconnectsAsync()
    {
        var identities = new HashSet<string> { First.InstanceId! };
        var independent = Second.Channel!;
        for (var cycle = 0; cycle < 32; cycle++)
        {
            var stale = First.Channel!;
            var pending = stale.InvokeAsync("wait");
            await First.DisconnectAsync();
            await ExpectDisposed(pending);
            await Reconnect(First);
            Check(identities.Add(First.InstanceId!), "unique stress connection ID");
            await ExpectDisposed(stale.InvokeAsync("getState"));
            Check(Convert.ToInt32((await State(First))["count"]) == 0, "stress instance starts clean");
            Check(
                await First.Channel!.InvokeAsync<string>("simulateTap") == "managed acknowledgement",
                "stress callback"
            );
            Check(ReferenceEquals(independent, Second.Channel), "other channel identity survives stress");
            Check((await State(Second))["uiThread"] is true, "other view survives stress");
        }
    }

    private static void CheckLayout(AvaloniaChannelView view) =>
        Check(
#if UNO_SAMPLE
            view.ActualWidth > 0 && view.ActualHeight > 0 && view.Channel is not null,
#else
            view.Bounds.Width > 0 && view.Bounds.Height > 0 && view.Channel is not null,
#endif
            "attached native view has nonzero bounds"
        );

    private static async Task<Dictionary<string, object?>> State(AvaloniaChannelView view) =>
        await view.Channel!.InvokeAsync<Dictionary<string, object?>>("getState")
        ?? throw new InvalidOperationException("Missing view state");

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException("Native view check failed: " + name);
    }

    private static async Task ExpectDisposed(Task task)
    {
        try
        {
            await task;
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        throw new InvalidOperationException("A stale view channel remained usable.");
    }
}
