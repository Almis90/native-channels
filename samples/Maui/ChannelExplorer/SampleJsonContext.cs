using System.Text.Json.Serialization;

namespace ChannelExplorer;

[JsonSerializable(typeof(AddArguments))]
[JsonSerializable(typeof(DeviceInfo))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class SampleJsonContext : JsonSerializerContext;
