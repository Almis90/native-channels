namespace NativeChannels;

/// <summary>Explicit native adapter entrypoints. AppleFramework is a bundled framework's simple name.</summary>
public sealed record NativeModuleRegistration(string AndroidClass, string AppleClass, string? AppleFramework = null)
{
#if WINDOWS
    /// <summary>Creates a fresh Windows adapter for each transport. The transport owns its disposal.</summary>
    public Func<IWindowsChannelModule>? WindowsFactory { get; init; }
#endif

    /// <summary>Creates a fresh Linux adapter for each transport. The transport owns its disposal.</summary>
    /// <remarks>Available on every target for portable registration code; used only by the Linux transport.</remarks>
    public Func<ILinuxChannelModule>? LinuxFactory { get; init; }

#if BROWSER
    /// <summary>Packaged ES module URL, relative to the document base. Exports register(context), returning a disposer.</summary>
    public string? BrowserModule { get; init; }
#endif
}
