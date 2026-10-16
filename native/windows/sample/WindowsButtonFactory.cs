using Microsoft.Maui.ApplicationModel;
using Microsoft.UI.Xaml;
using Button = Microsoft.UI.Xaml.Controls.Button;
using HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment;
using VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment;

namespace NativeChannels.Sample;

internal static class WindowsButtonFactory
{
    public static WindowsNativeView Create(WindowsViewContext context)
    {
        var parameters = (IDictionary<string, object?>?)context.CreationParameters;
        var title = parameters?["title"] as string ?? "Native Windows button";
        var button = new Button
        {
            Content = title,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var channel = context.Channels.GetMethodChannel(
            context.ChannelName,
            new() { HandlerDispatcher = MauiChannelDispatchers.MainThread }
        );
        var count = 0;
        async Task<string?> TapAsync(CancellationToken cancellation)
        {
            count++;
            return await channel.InvokeAsync<string>(
                "clicked",
                new Dictionary<string, object?> { ["instanceId"] = context.InstanceId, ["count"] = count },
                cancellation
            );
        }
        async void Clicked(object sender, RoutedEventArgs args)
        {
            try
            {
                await TapAsync(CancellationToken.None);
            }
            catch (ObjectDisposedException) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("Windows button callback failed: {0}", error);
            }
        }
        channel.SetMethodCallHandler(
            async (call, cancellation) =>
            {
                switch (call.Method)
                {
                    case "getState":
                        return new Dictionary<string, object?>
                        {
                            ["title"] = title,
                            ["count"] = count,
                            ["uiThread"] = MainThread.IsMainThread,
                        };
                    case "simulateTap":
                        return await TapAsync(cancellation);
                    case "wait":
                        await Task.Delay(Timeout.Infinite, cancellation);
                        return null;
                    default:
                        throw new MethodNotImplementedException(call.Method);
                }
            }
        );
        button.Click += Clicked;
        return new(
            button,
            () =>
            {
                button.Click -= Clicked;
                channel.Dispose();
            }
        );
    }
}
