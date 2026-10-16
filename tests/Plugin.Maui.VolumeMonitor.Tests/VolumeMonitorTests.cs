using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NativeChannels;
using Plugin.Maui.VolumeMonitor;
using Xunit;

namespace VolumeMonitor.Tests;

public sealed class VolumeMonitorTests
{
    [Fact]
    public async Task InitialLevelsUpdatesIndependentCancellationAndRestart()
    {
        await using var fixture = new Harness();
        using var firstCancellation = new CancellationTokenSource();
        using var secondCancellation = new CancellationTokenSource();
        await using var first = fixture.Monitor.ListenAsync(firstCancellation.Token).GetAsyncEnumerator();
        await using var second = fixture.Monitor.ListenAsync(secondCancellation.Token).GetAsyncEnumerator();
        Assert.True(await first.MoveNextAsync());
        Assert.Equal(0.5, first.Current);
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(0.5, second.Current);
        Assert.Equal(2, fixture.Active);
        firstCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.MoveNextAsync().AsTask());
        await fixture.WaitForActiveAsync(1);
        fixture.Publish(0.8);
        Assert.True(await second.MoveNextAsync());
        Assert.Equal(0.8, second.Current);
        secondCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.MoveNextAsync().AsTask());
        await fixture.WaitForActiveAsync(0);
        fixture.Publish(0.2);
        await using var restarted = fixture.Monitor.ListenAsync().GetAsyncEnumerator();
        Assert.True(await restarted.MoveNextAsync());
        Assert.Equal(0.2, restarted.Current);
    }

    [Fact]
    public async Task EarlyExitAndBridgeDisposalReleaseSubscriptions()
    {
        await using var fixture = new Harness();
        await foreach (var level in fixture.Monitor.ListenAsync())
        {
            Assert.Equal(0.5, level);
            break;
        }
        await fixture.WaitForActiveAsync(0);
        await using var listener = fixture.Monitor.ListenAsync().GetAsyncEnumerator();
        Assert.True(await listener.MoveNextAsync());
        var waiting = listener.MoveNextAsync().AsTask();
        await fixture.Client.DisposeAsync();
        await Assert.ThrowsAnyAsync<Exception>(() => waiting);
        await fixture.WaitForActiveAsync(0);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(null)]
    [InlineData("0.5")]
    [InlineData(1)]
    public async Task InvalidNativeValuesFailAndRemoveTheListener(object? invalid)
    {
        await using var fixture = new Harness();
        await using var listener = fixture.Monitor.ListenAsync().GetAsyncEnumerator();
        Assert.True(await listener.MoveNextAsync());
        fixture.Publish(invalid);
        await Assert.ThrowsAsync<ChannelSerializationException>(() => listener.MoveNextAsync().AsTask());
        await fixture.WaitForActiveAsync(0);
    }

    [Fact]
    public async Task PreCancelledEnumerationDoesNotRegister()
    {
        await using var fixture = new Harness();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await using var listener = fixture.Monitor.ListenAsync(cancelled.Token).GetAsyncEnumerator();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => listener.MoveNextAsync().AsTask());
        Assert.Equal(0, fixture.Active);
    }

    [Fact]
    public async Task NativeErrorsArePreservedAndCleanedUp()
    {
        await using var fixture = new Harness();
        await using var listener = fixture.Monitor.ListenAsync().GetAsyncEnumerator();
        Assert.True(await listener.MoveNextAsync());
        fixture.Fail(new ChannelException("volume_unavailable", "Cannot observe volume"));
        var failure = await Assert.ThrowsAsync<ChannelException>(() => listener.MoveNextAsync().AsTask());
        Assert.Equal("volume_unavailable", failure.Code);
        await fixture.WaitForActiveAsync(0);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ChannelService Client { get; }
        private ChannelService Server { get; }
        private readonly EventChannel _events;
        private readonly IDisposable _registration;
        private readonly object _gate = new();
        private readonly List<Channel<object?>> _sources = [];
        private object? _currentLevel = 0.5;
        public IVolumeMonitor Monitor { get; }
        public int Active
        {
            get
            {
                lock (_gate)
                    return _sources.Count;
            }
        }

        public Harness()
        {
            var (a, b) = InMemoryTransport.CreatePair();
            Client = new(new BinaryMessenger(a));
            Server = new(new BinaryMessenger(b));
            Monitor = new Plugin.Maui.VolumeMonitor.VolumeMonitor(Client);
            _events = Server.GetEventChannel(Plugin.Maui.VolumeMonitor.VolumeMonitor.ChannelName);
            _registration = _events.SetStreamHandler((_, cancellation) => Observe(cancellation));
        }

        private async IAsyncEnumerable<object?> Observe([EnumeratorCancellation] CancellationToken cancellation)
        {
            var source = Channel.CreateUnbounded<object?>();
            lock (_gate)
            {
                _sources.Add(source);
                source.Writer.TryWrite(_currentLevel);
            }
            try
            {
                await foreach (var level in source.Reader.ReadAllAsync(cancellation))
                    yield return level;
            }
            finally
            {
                lock (_gate)
                    _sources.Remove(source);
            }
        }

        public void Publish(object? level)
        {
            lock (_gate)
            {
                _currentLevel = level;
                foreach (var source in _sources)
                    source.Writer.TryWrite(level);
            }
        }

        public void Fail(Exception error)
        {
            lock (_gate)
                foreach (var source in _sources)
                    source.Writer.TryComplete(error);
        }

        public async Task WaitForActiveAsync(int count)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (Active != count)
                await Task.Delay(5, deadline.Token);
        }

        public async ValueTask DisposeAsync()
        {
            _registration.Dispose();
            await _events.DisposeAsync();
            await Client.DisposeAsync();
            await Server.DisposeAsync();
        }
    }
}
