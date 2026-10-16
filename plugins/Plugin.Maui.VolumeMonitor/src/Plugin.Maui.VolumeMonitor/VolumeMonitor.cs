using System.Runtime.CompilerServices;
using NativeChannels;

namespace Plugin.Maui.VolumeMonitor;

public interface IVolumeMonitor
{
    /// <summary>Initial volume followed by changes, normalized to 0–1. Each enumeration owns one native observer.</summary>
    IAsyncEnumerable<double> ListenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Observes media/output volume without changing the system volume or audio session.</summary>
public sealed class VolumeMonitor(IChannelService channels) : IVolumeMonitor
{
    public const string ChannelName = "plugin.maui.volume";

    public async IAsyncEnumerable<double> ListenAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var events = channels.GetEventChannel(ChannelName);
        await foreach (
            var value in events.ListenAsync<object>(cancellationToken: cancellationToken).ConfigureAwait(false)
        )
        {
            if (value is not double volume || !double.IsFinite(volume) || volume < 0 || volume > 1)
                throw new ChannelSerializationException("Expected a finite volume level between 0 and 1.");
            yield return volume;
        }
    }
}
