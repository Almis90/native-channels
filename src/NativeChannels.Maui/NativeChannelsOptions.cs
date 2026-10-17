using System.Text.Json;

namespace NativeChannels;

public sealed class NativeChannelsOptions
{
    public IList<NativeModuleRegistration> Modules { get; } = new List<NativeModuleRegistration>();
    public IList<NativeViewFactoryRegistration> ViewFactories { get; } = new List<NativeViewFactoryRegistration>();
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public ChannelLimits Limits { get; set; } = new();
    public JsonSerializerOptions Json { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>Optional metadata-only diagnostics for the service and native view channels. Disabled by default.</summary>
    public ChannelDiagnostics? Diagnostics { get; set; }
}
