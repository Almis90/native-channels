#if WINDOWS
using Microsoft.UI.Xaml;

namespace NativeChannels;

/// <summary>The isolated adapter endpoint, identity, and decoded parameters for a Windows view.</summary>
public sealed record WindowsViewContext(
    IChannelService Channels,
    string InstanceId,
    string ChannelName,
    object? CreationParameters
);

/// <summary>Owns a WinUI element and its listeners. Cleanup runs once on the UI thread.</summary>
public sealed class WindowsNativeView : IDisposable
{
    private Action? _close;
    public UIElement View { get; }

    public WindowsNativeView(UIElement view, Action? close = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        View = view;
        _close = close;
    }

    public void Close() => Interlocked.Exchange(ref _close, null)?.Invoke();

    public void Dispose() => Close();
}
#endif
