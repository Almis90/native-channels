using System.Text.Json;

namespace NativeChannels;

/// <summary>Framework-independent configuration for one registered native channel session.</summary>
public class ChannelRegistrationOptions
{
    public IList<NativeModuleRegistration> Modules { get; } = new List<NativeModuleRegistration>();
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public ChannelLimits Limits { get; set; } = new();
    public JsonSerializerOptions Json { get; set; } = new(JsonSerializerDefaults.Web);

    /// <summary>Optional metadata-only diagnostics for this session. Disabled by default.</summary>
    public ChannelDiagnostics? Diagnostics { get; set; }
}
