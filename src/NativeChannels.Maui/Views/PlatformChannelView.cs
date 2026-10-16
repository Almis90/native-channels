using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;

namespace NativeChannels;

/// <summary>Hosts an explicitly registered native view and exposes its connection's method channel.</summary>
public sealed class PlatformChannelView : View
{
    public static readonly BindableProperty FactoryNameProperty = BindableProperty.Create(
        nameof(FactoryName),
        typeof(string),
        typeof(PlatformChannelView),
        "",
        propertyChanging: CheckConfigurationChange
    );
    public static readonly BindableProperty CreationParametersProperty = BindableProperty.Create(
        nameof(CreationParameters),
        typeof(object),
        typeof(PlatformChannelView),
        null,
        propertyChanging: CheckConfigurationChange
    );

    /// <summary>Registered factory name. Set before connecting; comparison is case-sensitive.</summary>
    public string FactoryName
    {
        get => (string)GetValue(FactoryNameProperty);
        set => SetValue(FactoryNameProperty, value);
    }

    /// <summary>JSON-codec value encoded once when each native instance is created.</summary>
    public object? CreationParameters
    {
        get => GetValue(CreationParametersProperty);
        set => SetValue(CreationParametersProperty, value);
    }

    /// <summary>Unique identifier for this connection, or null while disconnected.</summary>
    public string? InstanceId { get; private set; }

    /// <summary>View-owned channel. Replaced on reconnect and disposed on detach.</summary>
    public MethodChannel? Channel { get; private set; }

    /// <summary>Raised on the UI thread after a new channel is available. Register incoming handlers here.</summary>
    public event EventHandler? Connected;

    /// <summary>Raised on the UI thread after invalidating the old connection.</summary>
    public event EventHandler? Disconnected;

    /// <summary>Recreates the native instance and invalidates the previous channel. Requires the UI thread.</summary>
    public void Reconnect()
    {
        RequireMainThread();
        if (Handler is not PlatformChannelViewHandler handler)
            throw new InvalidOperationException("The view must have a MAUI handler before reconnecting.");
        handler.ReconnectView();
    }

    /// <summary>Removes native content and awaits bridge cleanup. Call Reconnect to create it again.</summary>
    public Task DisconnectAsync()
    {
        RequireMainThread();
        return Handler is PlatformChannelViewHandler handler ? handler.DisconnectView() : Task.CompletedTask;
    }

    internal void SetConnection(ViewChannelLease? lease)
    {
        InstanceId = lease?.InstanceId;
        Channel = lease?.Channel;
        OnPropertyChanged(nameof(InstanceId));
        OnPropertyChanged(nameof(Channel));
        if (lease is null)
            Disconnected?.Invoke(this, EventArgs.Empty);
        else
            Connected?.Invoke(this, EventArgs.Empty);
    }

    internal static void RequireMainThread()
    {
        if (!MainThread.IsMainThread)
            throw new InvalidOperationException("Native view operations require the UI thread.");
    }

    private static void CheckConfigurationChange(BindableObject bindable, object oldValue, object newValue)
    {
        RequireMainThread();
        if (((PlatformChannelView)bindable).Channel is not null && !Equals(oldValue, newValue))
            throw new InvalidOperationException(
                "Disconnect the native view before changing its factory or creation parameters."
            );
    }
}
