namespace NativeChannels;

/// <summary>Explicit native adapter entrypoints. AppleFramework is a bundled framework's simple name.</summary>
public sealed record NativeModuleRegistration(string AndroidClass, string AppleClass, string? AppleFramework = null)
{
#if WINDOWS
    /// <summary>Creates a fresh Windows adapter for each transport. The transport owns its disposal.</summary>
    public Func<IWindowsChannelModule>? WindowsFactory { get; init; }
#endif
}
