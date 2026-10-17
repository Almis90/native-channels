using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace ChannelExplorer;

internal sealed class ViewVerification
{
    public PlatformChannelView First { get; } = Create("First native button");
    public PlatformChannelView Second { get; } = Create("Second native button");
    public VerticalStackLayout Panel { get; } = new() { Spacing = 12 };
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

    private static PlatformChannelView Create(string title) =>
        new()
        {
            FactoryName = "sample.button",
            HeightRequest = 72,
            CreationParameters = new Dictionary<string, object?> { ["title"] = title },
        };

    private void Bind(PlatformChannelView view, TaskCompletionSource ready)
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
        First.Reconnect();
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
        First.Reconnect();
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
        return "native views: instances, creation arguments, callbacks, reconnect, unload/reload, stale channels, cancellation and UI dispatch; view stress: 32 reconnects";
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
            First.Reconnect();
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

    internal Task ProfileCycleAsync() =>
        MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var stale = First.Channel!;
            var oldId = First.InstanceId;
            await First.DisconnectAsync();
            First.Reconnect();
            Check(First.InstanceId != oldId, "profile fresh view ID");
            await ExpectDisposed(stale.InvokeAsync("getState"));
            Check((await State(First))["uiThread"] is true, "profile native UI dispatch");
            Check(
                await First.Channel!.InvokeAsync<string>("simulateTap") == "managed acknowledgement",
                "profile callback"
            );
        });

    private static void CheckLayout(PlatformChannelView view)
    {
#if ANDROID
        var host = (Android.Views.ViewGroup)view.Handler!.PlatformView!;
        var child = host.GetChildAt(0)!;
        Check(host.Width > 0 && child.Width == host.Width && child.Height == host.Height, "native view fills host");
#elif WINDOWS
        var host = (Microsoft.UI.Xaml.Controls.Grid)view.Handler!.PlatformView!;
        var child = (Microsoft.UI.Xaml.FrameworkElement)host.Children[0];
        Check(
            host.ActualWidth > 0 && child.ActualWidth == host.ActualWidth && child.ActualHeight == host.ActualHeight,
            "native view fills host"
        );
#else
        var host = (UIKit.UIView)view.Handler!.PlatformView!;
        Check(host.Bounds.Width > 0 && host.Subviews[0].Frame == host.Bounds, "native view fills host");
#endif
    }

    private static async Task<Dictionary<string, object?>> State(PlatformChannelView view) =>
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
