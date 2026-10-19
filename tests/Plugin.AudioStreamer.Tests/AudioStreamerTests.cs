using System.Buffers.Binary;
using System.Collections.Concurrent;
using AudioStreamer.Sample;
using NativeChannels;
using Plugin.AudioStreamer;
using Xunit;
using Streamer = Plugin.AudioStreamer.AudioStreamer;

namespace AudioStreamer.Tests;

public sealed class AudioStreamerTests
{
    [Fact]
    public void MusicPreservesPcmAcrossChunkBoundariesAndFadesToSilence()
    {
        var whole = new MusicGenerator().Next(1920, 0.16);
        var split = new MusicGenerator();
        Assert.Equal(whole, split.Next(960, 0.16).Concat(split.Next(960, 0.16)).ToArray());
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(whole));
        Assert.Contains(whole.Chunk(2), frame => Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(frame)) > 1000);
        var end = split.Next(960, 0.16, fadeOut: true);
        Assert.Equal((short)0, BinaryPrimitives.ReadInt16LittleEndian(end.AsSpan(end.Length - 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => split.Next(1, double.NaN));
    }

    [Fact]
    public void MusicLoopsWithoutLosingFramesAndRespectsSignalLevel()
    {
        var generator = new MusicGenerator();
        var first = generator.Next(960, 0.25);
        // Deliberately cross the loop boundary within a chunk.
        var remaining = MusicGenerator.LoopFrames - 960 - 100;
        while (remaining > 0)
        {
            var frames = Math.Min(4800, remaining);
            var chunk = generator.Next(frames, 0.25);
            foreach (var frame in chunk.Chunk(2))
                Assert.InRange(BinaryPrimitives.ReadInt16LittleEndian(frame), -8192, 8192);
            remaining -= frames;
        }
        var crossing = generator.Next(1060, 0.25);
        Assert.Equal(first, crossing[200..]);
        Assert.All(new MusicGenerator().Next(960, 0), value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public async Task BinaryTransferRetriesOnlyRejectedChunksAndKeepsPcmExact()
    {
        await using var harness = new NativeHarness();
        await using var audio = new Streamer(harness.Client);
        await using var session = await audio.OpenAsync();
        harness.Replies.Enqueue(1);
        var pcm = new byte[] { 0, 0, 255, 127, 0, 128 };
        await session.WriteAsync(pcm);
        Assert.Equal(2, harness.Attempts);
        Assert.Equal(pcm, Assert.Single(harness.Accepted));
        await Assert.ThrowsAsync<ArgumentException>(() => session.WriteAsync(new byte[1]));
        await Assert.ThrowsAsync<ArgumentException>(() => session.WriteAsync(new byte[9602]));
        Assert.Equal(2, harness.Attempts);
    }

    [Fact]
    public async Task PausedWriterCanBeCancelledAndResumeContinues()
    {
        await using var harness = new NativeHarness();
        await using var audio = new Streamer(harness.Client);
        await using var session = await audio.OpenAsync();
        await session.PauseAsync();
        using var cancel = new CancellationTokenSource();
        var blocked = session.WriteAsync(new byte[1920], cancel.Token);
        await harness.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(blocked.IsCompleted);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.Empty(harness.Accepted);
        await session.ResumeAsync();
        await session.WriteAsync(new byte[1920]);
        Assert.Single(harness.Accepted);
    }

    [Fact]
    public async Task StopUnblocksWriterAndNewSessionHasNewIdentity()
    {
        await using var harness = new NativeHarness();
        await using var audio = new Streamer(harness.Client);
        var first = await audio.OpenAsync();
        var old = harness.Identity!.ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => audio.OpenAsync());
        await first.PauseAsync();
        var blocked = first.WriteAsync(new byte[1920]);
        await harness.Attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => blocked);
        Assert.Equal(1, harness.Stops);
        await first.DisposeAsync();
        Assert.Equal(1, harness.Stops);
        await using var second = await audio.OpenAsync();
        Assert.NotEqual(old, harness.Identity);
        await second.WriteAsync(new byte[1920]);
    }

    [Fact]
    public async Task BridgeDisposalReleasesNativeLease()
    {
        await using var harness = new NativeHarness();
        await using var audio = new Streamer(harness.Client);
        _ = await audio.OpenAsync();
        await harness.Client.DisposeAsync();
        await harness.Closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NativeErrorsAndInvalidAcknowledgementsAreNotRetried()
    {
        await using var harness = new NativeHarness();
        await using var audio = new Streamer(harness.Client);
        await using var session = await audio.OpenAsync();
        harness.Replies.Enqueue(4);
        var error = await Assert.ThrowsAsync<ChannelException>(() => session.WriteAsync(new byte[2]));
        Assert.Equal("audio_rejected", error.Code);
        Assert.Equal(1, harness.Attempts);
        harness.MalformedReply = true;
        await Assert.ThrowsAsync<ChannelSerializationException>(() => session.WriteAsync(new byte[2]));
        Assert.Equal(2, harness.Attempts);
    }

    [Fact]
    public async Task FailedStartupCleanupDoesNotPoisonTheNextAttempt()
    {
        await using var harness = new NativeHarness { RejectNextOpen = true, RejectNextStop = true };
        await using var audio = new Streamer(harness.Client);
        await Assert.ThrowsAsync<ChannelException>(() => audio.OpenAsync());
        await using var session = await audio.OpenAsync();
        await session.WriteAsync(new byte[2]);
        Assert.Single(harness.Accepted);
    }

    private sealed class NativeHarness : IAsyncDisposable
    {
        public ChannelService Client { get; }
        private readonly ChannelService _server;
        public readonly ConcurrentQueue<byte> Replies = new();
        public readonly List<byte[]> Accepted = [];
        public readonly TaskCompletionSource Attempted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TaskCompletionSource _lease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public byte[]? Identity;
        public int Attempts;
        public int Stops;
        public bool MalformedReply;
        public bool RejectNextOpen;
        public bool RejectNextStop;
        private string _state = "stopped";

        public NativeHarness()
        {
            var (a, b) = InMemoryTransport.CreatePair();
            Client = new(new BinaryMessenger(a));
            _server = new(new BinaryMessenger(b));
            var control = _server.GetMethodChannel(AudioFormat.ControlChannel);
            control.SetMethodCallHandler(
                async (call, cancellation) =>
                {
                    switch (call.Method)
                    {
                        case "open":
                            if (RejectNextOpen)
                            {
                                RejectNextOpen = false;
                                throw new ChannelException("audio_focus_denied", "Audio focus denied");
                            }
                            Identity = call.GetArgument<byte[]>("session");
                            _state = "playing";
                            _lease = new(TaskCreationOptions.RunContinuationsAsynchronously);
                            try
                            {
                                await _lease.Task.WaitAsync(cancellation);
                            }
                            finally
                            {
                                Closed.TrySetResult();
                            }
                            return null;
                        case "status":
                            return new Dictionary<string, object?>
                            {
                                ["state"] = _state,
                                ["bufferedFrames"] = 0,
                                ["capacityFrames"] = 24000,
                                ["playedFrames"] = 0L,
                            };
                        case "pause":
                            _state = "paused";
                            return null;
                        case "resume":
                            _state = "playing";
                            return null;
                        case "stop":
                            if (RejectNextStop)
                            {
                                RejectNextStop = false;
                                throw new ChannelException("audio_session_mismatch", "Another session is active");
                            }
                            Stops++;
                            _lease.TrySetResult();
                            _state = "stopped";
                            return null;
                        default:
                            throw new MethodNotImplementedException();
                    }
                }
            );
            var messages = _server.GetBasicMessageChannel(
                AudioFormat.DataChannel,
                new() { Codec = BinaryMessageCodec.Instance }
            );
            messages.SetMessageHandler(
                (value, _) =>
                {
                    Attempts++;
                    var payload = (byte[])value!;
                    Assert.Equal(Identity, payload[..16]);
                    Attempted.TrySetResult();
                    if (MalformedReply)
                        return ValueTask.FromResult<object?>(new byte[] { 0, 0 });
                    var code =
                        _state == "paused" ? (byte)2
                        : Replies.TryDequeue(out var reply) ? reply
                        : (byte)0;
                    if (code == 0)
                        Accepted.Add(payload[16..]);
                    return ValueTask.FromResult<object?>(new byte[] { code });
                }
            );
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            await _server.DisposeAsync();
        }
    }
}
