using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NativeChannels.Windows.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public async Task RegistrationIsLazyAndEachProviderOwnsOneIndependentSession()
    {
        var modules = new List<Module>();
        var services = new ServiceCollection();
        services.AddNativeChannels(options =>
            options.Modules.Add(
                Registration(() =>
                {
                    var module = new Module();
                    modules.Add(module);
                    return module;
                })
            )
        );
        await using var first = services.BuildServiceProvider();
        await using var second = services.BuildServiceProvider();
        Assert.Empty(modules);

        var firstChannels = first.GetRequiredService<IChannelService>();
        Assert.Same(firstChannels, first.GetRequiredService<IChannelService>());
        var secondChannels = second.GetRequiredService<IChannelService>();
        Assert.NotSame(firstChannels, secondChannels);
        Assert.Equal(2, modules.Count);
        using var firstHandle = firstChannels.GetMethodChannel("probe");
        using var secondHandle = secondChannels.GetMethodChannel("probe");
        Assert.Equal(42, await firstHandle.InvokeAsync<int>("echo", 42));

        await first.DisposeAsync();
        await first.DisposeAsync();
        Assert.Equal(1, modules[0].Disposals);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => firstHandle.InvokeAsync("echo"));
        Assert.Equal(43, await secondHandle.InvokeAsync<int>("echo", 43));
        Assert.Equal(0, modules[1].Disposals);
        await second.DisposeAsync();
        Assert.Equal(1, modules[1].Disposals);
    }

    [Fact]
    public async Task RegistrationSnapshotsModulesLimitsTimeoutAndJsonSettings()
    {
        var module = new Module();
        var json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        var options = new ChannelRegistrationOptions
        {
            DefaultTimeout = Timeout.InfiniteTimeSpan,
            Limits = new() { MaximumNameBytes = 8 },
            Json = json,
        };
        options.Modules.Add(Registration(() => module));
        var services = new ServiceCollection().AddNativeChannels(options);
        options.Modules.Clear();
        options.DefaultTimeout = TimeSpan.Zero;
        options.Limits = new() { MaximumNameBytes = 1 };
        json.PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower;
        options.Json = new();

        await using var provider = services.BuildServiceProvider();
        var channels = provider.GetRequiredService<IChannelService>();
        using var handle = channels.GetMethodChannel("probe");
        var value = await handle.InvokeAsync<Dictionary<string, object?>>("echo", new Payload("kept"));
        Assert.Equal("kept", value!["display_name"]);
        Assert.Throws<ArgumentException>(() => channels.GetMethodChannel("too-long-name"));
    }

    [Fact]
    public void DuplicateRegistrationDoesNotRunConfigurationOrChangeServices()
    {
        var services = new ServiceCollection().AddNativeChannels();
        var count = services.Count;
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNativeChannels(_ => throw new Exception("must not run"))
        );
        Assert.Throws<InvalidOperationException>(() => services.AddNativeChannels(new ChannelRegistrationOptions()));
        Assert.Equal(count, services.Count);
    }

    [Theory]
    [InlineData(typeof(IChannelService))]
    [InlineData(typeof(IBinaryMessenger))]
    public void ExistingChannelServicesAreNotSilentlyReplaced(Type serviceType)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(ServiceDescriptor.Singleton(serviceType, _ => throw new Exception("must not resolve")));
        Assert.Throws<InvalidOperationException>(() => services.AddNativeChannels());
        Assert.Single(services);
    }

    [Fact]
    public void FailedConfigurationLeavesTheCollectionUsable()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddNativeChannels(_ => throw new InvalidOperationException())
        );
        Assert.Empty(services);
        services.AddNativeChannels();
    }

    [Theory]
    [InlineData("timeout-zero")]
    [InlineData("timeout-negative")]
    [InlineData("timeout-overflow")]
    [InlineData("payload")]
    [InlineData("depth")]
    [InlineData("name")]
    public void InvalidSettingsFailBeforeRegisteringOrCreatingNativeModules(string setting)
    {
        var options = new ChannelRegistrationOptions();
        switch (setting)
        {
            case "timeout-zero":
                options.DefaultTimeout = TimeSpan.Zero;
                break;
            case "timeout-negative":
                options.DefaultTimeout = TimeSpan.FromMilliseconds(-2);
                break;
            case "timeout-overflow":
                options.DefaultTimeout = TimeSpan.MaxValue;
                break;
            case "payload":
                options.Limits = new() { MaximumPayloadBytes = 0 };
                break;
            case "depth":
                options.Limits = new() { MaximumDepth = 0 };
                break;
            case "name":
                options.Limits = new() { MaximumNameBytes = 0 };
                break;
        }
        options.Modules.Add(Registration(() => throw new Exception("must not initialize")));
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddNativeChannels(options));
        Assert.Empty(services);
    }

    [Fact]
    public async Task ProviderDisposalCompletesActiveCallsAndReleasesTheNativeModuleOnce()
    {
        var module = new Module();
        var services = new ServiceCollection();
        services.AddNativeChannels(options => options.Modules.Add(Registration(() => module)));
        await using var provider = services.BuildServiceProvider();
        using var handle = provider.GetRequiredService<IChannelService>().GetMethodChannel("probe");
        var pending = handle.InvokeAsync("wait");
        await module.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        await module.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, module.Disposals);
    }

    [Fact]
    public async Task HostDispatcherStillHandlesIncomingCalls()
    {
        var services = new ServiceCollection();
        services.AddNativeChannels(options => options.Modules.Add(Registration(() => new Module())));
        await using var provider = services.BuildServiceProvider();
        var dispatched = 0;
        using var handle = provider
            .GetRequiredService<IChannelService>()
            .GetMethodChannel(
                "probe",
                new()
                {
                    HandlerDispatcher = (handler, cancellation) =>
                    {
                        cancellation.ThrowIfCancellationRequested();
                        dispatched++;
                        return handler();
                    },
                }
            );
        using var registration = handle.SetMethodCallHandler((call, _) => ValueTask.FromResult(call.Arguments));
        Assert.Equal(42, await handle.InvokeAsync<int>("callback", 42));
        Assert.Equal(1, dispatched);
    }

    private static NativeModuleRegistration Registration(Func<IWindowsChannelModule> factory) =>
        new("", "") { WindowsFactory = factory };

    public sealed record Payload(string DisplayName);

    private sealed class Module : IWindowsChannelModule
    {
        private MethodChannel? _channel;
        public int Disposals { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Register(IChannelService channels)
        {
            _channel = channels.GetMethodChannel("probe");
            _channel.SetMethodCallHandler(
                async (call, cancellation) =>
                {
                    if (call.Method == "wait")
                    {
                        using var registration = cancellation.Register(() => Cancelled.TrySetResult());
                        Entered.TrySetResult();
                        await Task.Delay(Timeout.Infinite, cancellation);
                    }
                    if (call.Method == "callback")
                    {
                        return await _channel.InvokeAsync<object>("managed", call.Arguments, cancellation);
                    }
                    return call.Arguments;
                }
            );
        }

        public void Dispose()
        {
            Disposals++;
            _channel?.Dispose();
        }
    }
}
