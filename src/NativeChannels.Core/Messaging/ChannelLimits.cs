using System.Text;

namespace NativeChannels;

public sealed record ChannelLimits
{
    public int MaximumPayloadBytes { get; init; } = 16 * 1024 * 1024;
    public int MaximumDepth { get; init; } = 64;
    public int MaximumNameBytes { get; init; } = 256;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumNameBytes);
    }

    public void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Any(char.IsControl) || Encoding.UTF8.GetByteCount(name) > MaximumNameBytes)
        {
            throw new ArgumentException(
                "Name contains control characters or exceeds the configured UTF-8 limit.",
                nameof(name)
            );
        }
    }

    public void ValidatePayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.Length > MaximumPayloadBytes)
        {
            throw new ChannelSerializationException("Payload exceeds the configured byte limit.");
        }
    }

    internal static void ValidateTimeout(TimeSpan timeout)
    {
        if (
            timeout != Timeout.InfiniteTimeSpan
            && (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
        )
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "Use a positive supported timer interval or Timeout.InfiniteTimeSpan."
            );
        }
    }
}
