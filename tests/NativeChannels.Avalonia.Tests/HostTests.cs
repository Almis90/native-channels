using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(NativeChannels.Avalonia.Tests.TestApplication))]

namespace NativeChannels.Avalonia.Tests;

public sealed class TestApplication : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder
            .Configure<TestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseNativeChannels(HostTests.ConfigureChannels);
}

public sealed class HostTests
{
    internal static void ConfigureChannels(IServiceCollection services) =>
        services.AddSingleton<IChannelService>(_ => new ChannelService(
            new BinaryMessenger(InMemoryTransport.CreatePair().Managed)
        ));

    [AvaloniaFact]
    public async Task BuilderAttachesHostAndSessionsAreIndependent()
    {
        var host = Application.Current!.GetNativeChannels();
        await using var first = host.CreateSession();
        await using var second = host.CreateSession();
        Assert.NotSame(first.Channels, second.Channels);
        await first.DisposeAsync();
        Assert.True(first.Stopping.IsCancellationRequested);
        Assert.False(second.Stopping.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => first.Services);
        using var channel = second.Channels.GetMethodChannel("still-alive");
    }

    [AvaloniaFact]
    public void RegistrationErrorsAreActionableAndRetryDoesNotPoisonBuilder()
    {
        var builder = AppBuilder.Configure<TestApplication>();
        Assert.Contains(
            "AddNativeChannels",
            Assert.Throws<InvalidOperationException>(() => builder.UseNativeChannels(_ => { })).Message
        );
        builder.UseNativeChannels(ConfigureChannels);
        Assert.Throws<InvalidOperationException>(() => builder.UseNativeChannels(ConfigureChannels));
        Assert.Contains(
            "UseNativeChannels",
            Assert.Throws<InvalidOperationException>(() => new Application().GetNativeChannels()).Message
        );
        Assert.Throws<InvalidOperationException>(() =>
            AvaloniaChannelHost.Create(services =>
            {
                ConfigureChannels(services);
                ConfigureChannels(services);
            })
        );
    }

    [AvaloniaFact]
    public async Task ConfigurationIsSnapshottedAndSingletonsBelongToSessions()
    {
        IServiceCollection? captured = null;
        await using var host = AvaloniaChannelHost.Create(services =>
        {
            captured = services;
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe());
        });
        captured!.Clear();
        var first = host.CreateSession();
        var second = host.CreateSession();
        var one = first.Services.GetRequiredService<Probe>();
        var two = second.Services.GetRequiredService<Probe>();
        Assert.NotSame(one, two);
        var disposal = host.DisposeAsync().AsTask();
        Assert.Same(disposal, host.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(() => host.CreateSession());
        await disposal;
        Assert.Equal(1, one.Disposed);
        Assert.Equal(1, two.Disposed);
        Assert.True(one.OnUIThread);
    }

    [AvaloniaFact]
    public async Task RepeatedSessionDisposalWaitsForTheSameAsyncCleanup()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var host = AvaloniaChannelHost.Create(services =>
        {
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe(gate.Task));
        });
        var session = host.CreateSession();
        var probe = session.Services.GetRequiredService<Probe>();
        var first = session.DisposeAsync().AsTask();
        Assert.Same(first, session.DisposeAsync().AsTask());
        Assert.False(first.IsCompleted);
        Assert.True(session.Stopping.IsCancellationRequested);
        gate.SetResult();
        await first;
        Assert.Equal(1, probe.Disposed);
    }

    [AvaloniaFact]
    public async Task RecreatedSessionsReleaseTheirProvidersAndRejectSharedChannelInstances()
    {
        await using var external = new ChannelService(new BinaryMessenger(InMemoryTransport.CreatePair().Managed));
        Assert.Throws<InvalidOperationException>(() =>
            AvaloniaChannelHost.Create(services => services.AddSingleton<IChannelService>(external))
        );
        Assert.Throws<InvalidOperationException>(() =>
            AvaloniaChannelHost.Create(services => services.AddTransient<IChannelService>(_ => external))
        );
        await using var host = AvaloniaChannelHost.Create(services =>
        {
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe());
        });
        for (var cycle = 0; cycle < 32; cycle++)
        {
            var session = host.CreateSession();
            var probe = session.Services.GetRequiredService<Probe>();
            using var channel = session.Channels.GetMethodChannel("recreated");
            await session.DisposeAsync();
            Assert.Equal(1, probe.Disposed);
            Assert.True(session.Stopping.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => session.Channels);
        }
    }

    [AvaloniaFact]
    public async Task HostCleansOtherSessionsAfterCancellationCallbackAndServiceFailures()
    {
        var host = AvaloniaChannelHost.Create(services =>
        {
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe(fail: true));
        });
        var first = host.CreateSession();
        var second = host.CreateSession();
        var one = first.Services.GetRequiredService<Probe>();
        var two = second.Services.GetRequiredService<Probe>();
        using var registration = first.Stopping.Register(() => throw new InvalidOperationException("callback"));
        await Assert.ThrowsAnyAsync<Exception>(() => host.DisposeAsync().AsTask());
        Assert.Equal(1, one.Disposed);
        Assert.Equal(1, two.Disposed);
        Assert.True(second.Stopping.IsCancellationRequested);
        Assert.Throws<ObjectDisposedException>(() => first.Channels.GetMethodChannel("closed"));
    }

    [AvaloniaFact]
    public async Task FailedServiceResolutionStillHasAnOwnedProvider()
    {
        await using var host = AvaloniaChannelHost.Create(services =>
        {
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe());
            services.AddSingleton<string>(provider =>
            {
                _ = provider.GetRequiredService<Probe>();
                throw new InvalidOperationException("startup");
            });
        });
        var session = host.CreateSession();
        Assert.Throws<InvalidOperationException>(() => session.Services.GetRequiredService<string>());
        var probe = session.Services.GetRequiredService<Probe>();
        await session.DisposeAsync();
        Assert.Equal(1, probe.Disposed);
    }

    [AvaloniaFact]
    public async Task DisposalFromWorkerStartsCleanupOnUIThread()
    {
        await using var host = AvaloniaChannelHost.Create(services =>
        {
            ConfigureChannels(services);
            services.AddSingleton(_ => new Probe());
        });
        var session = host.CreateSession();
        var probe = session.Services.GetRequiredService<Probe>();
        await Task.Run(async () => await session.DisposeAsync());
        Assert.True(probe.OnUIThread);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() => host.CreateSession()));
    }

    [AvaloniaFact]
    public async Task ShutdownCompletesPendingCallsEvenIfAnApplicationServiceThrows()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var remote = native.GetMethodChannel("pending");
        using var registration = remote.SetMethodCallHandler(
            async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.Infinite, token);
                return null;
            }
        );
        var host = AvaloniaChannelHost.Create(services =>
        {
            services.AddSingleton<IChannelService>(_ => new ChannelService(new BinaryMessenger(pair.Managed)));
            services.AddSingleton(_ => new Probe(fail: true));
        });
        var session = host.CreateSession();
        _ = session.Services.GetRequiredService<Probe>();
        using var client = session.Channels.GetMethodChannel("pending");
        var pending = client.InvokeAsync("wait");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<Exception>(() => host.DisposeAsync().AsTask());
        await Assert.ThrowsAnyAsync<Exception>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(pending.IsCompleted);
    }

    [AvaloniaFact]
    public async Task DispatcherPreservesStartOrderAndAllowsReentrantAsyncWork()
    {
        var starts = new List<int>();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continueFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = AvaloniaChannelDispatchers
            .UIThread(
                async () =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    starts.Add(1);
                    firstEntered.SetResult();
                    await continueFirst.Task;
                    return await AvaloniaChannelDispatchers.UIThread(() => ValueTask.FromResult<object?>(3), default);
                },
                default
            )
            .AsTask();
        var second = AvaloniaChannelDispatchers
            .UIThread(
                () =>
                {
                    Assert.True(Dispatcher.UIThread.CheckAccess());
                    starts.Add(2);
                    continueFirst.SetResult();
                    return ValueTask.FromResult<object?>(2);
                },
                default
            )
            .AsTask();
        await firstEntered.Task;
        Assert.Equal(2, await second);
        Assert.Equal(3, await first);
        Assert.Equal(new[] { 1, 2 }, starts);
    }

    [AvaloniaFact]
    public async Task CancelledQueuedHandlerNeverStartsAndRunningDispatchCanBeCancelled()
    {
        using var queued = new CancellationTokenSource();
        var invoked = false;
        var pending = AvaloniaChannelDispatchers
            .UIThread(
                () =>
                {
                    invoked = true;
                    return ValueTask.FromResult<object?>(null);
                },
                queued.Token
            )
            .AsTask();
        queued.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await Dispatcher.UIThread.InvokeAsync(() => { });
        Assert.False(invoked);

        using var running = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = AvaloniaChannelDispatchers
            .UIThread(
                () =>
                {
                    entered.SetResult();
                    return new ValueTask<object?>(release.Task);
                },
                running.Token
            )
            .AsTask();
        await entered.Task;
        running.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        release.SetException(new InvalidOperationException("late handler failure"));
    }

    [AvaloniaFact]
    public async Task PluginCleanupCanSendItsFinalNativeRequestBeforeChannelsStop()
    {
        var pair = InMemoryTransport.CreatePair();
        await using var native = new ChannelService(new BinaryMessenger(pair.Native));
        using var remote = native.GetMethodChannel("cleanup");
        var stopped = false;
        using var registration = remote.SetMethodCallHandler(
            (_, _) =>
            {
                stopped = true;
                return ValueTask.FromResult<object?>(null);
            }
        );
        var host = AvaloniaChannelHost.Create(services =>
        {
            services.AddSingleton<IChannelService>(_ => new ChannelService(new BinaryMessenger(pair.Managed)));
            services.AddSingleton(provider => new ShutdownRequest(provider.GetRequiredService<IChannelService>()));
        });
        var session = host.CreateSession();
        _ = session.Services.GetRequiredService<ShutdownRequest>();
        await host.DisposeAsync();
        Assert.True(stopped);
    }

    private sealed class ShutdownRequest(IChannelService channels) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            using var channel = channels.GetMethodChannel("cleanup");
            await channel.InvokeAsync("stop");
        }
    }

    private sealed class Probe(Task? gate = null, bool fail = false) : IAsyncDisposable
    {
        public int Disposed { get; private set; }
        public bool OnUIThread { get; private set; }

        public async ValueTask DisposeAsync()
        {
            Disposed++;
            OnUIThread = Dispatcher.UIThread.CheckAccess();
            if (gate is not null)
                await gate;
            if (fail)
                throw new InvalidOperationException("service cleanup");
        }
    }
}
