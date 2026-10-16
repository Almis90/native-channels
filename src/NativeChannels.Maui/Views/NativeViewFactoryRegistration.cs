namespace NativeChannels;

/// <summary>Maps a view type to explicit Kotlin/Swift factory entrypoints.</summary>
public sealed record NativeViewFactoryRegistration(
    string Name,
    string AndroidClass,
    string AppleClass,
    string? AppleFramework = null
)
{
#if WINDOWS
    /// <summary>Creates an owned WinUI view on the UI thread for each connection.</summary>
    public Func<WindowsViewContext, WindowsNativeView>? WindowsFactory { get; init; }
#endif
}
