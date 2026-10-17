# Avalonia host

`NativeChannels.Avalonia` is the reusable application host for Avalonia 12.1.4 and .NET 10.
It supplies application registration, independent owned sessions and asynchronous UI dispatch.
Its portable `net10.0` package depends on Core, Avalonia and Microsoft DI. Platform transports
and plugin assets come from the platform packages selected by the application head.

This is development work for 0.2, not a published package or complete Avalonia acceptance.
`AvaloniaChannelView` now owns native/DOM view attachment, per-instance channels and
asynchronous disposal. The [reference samples](../samples/Avalonia/README.md) implement
mobile suspension and browser page hooks explicitly; they are sample policies rather than
automatic application-wide behavior. New heads and adapters still need runtime verification.

## Register

Reference `NativeChannels.Avalonia` and the plugin/platform packages in the application.
Configure the AppBuilder before starting Avalonia:

```csharp
AppBuilder.Configure<App>()
    .UsePlatformDetect()
    .UseNativeChannels(services =>
    {
        services.AddNativeChannels(options => options.AddCurrency());
        services.AddCurrency();
    });
```

`UseNativeChannels` configures services once and attaches the host before
`OnFrameworkInitializationCompleted`. Repeated registration on the same builder fails.
Core has no Avalonia dependency. No MAUI dependency is required by this host.

## Own sessions

From `OnFrameworkInitializationCompleted` or later:

```csharp
var host = this.GetNativeChannels();
var session = host.CreateSession();
var currency = session.Services.GetRequiredService<ICurrencyService>();
```

Create sessions and resolve their first service on the Avalonia UI thread. Each session owns
a separate DI provider, including its singleton channel service and plugin instances. Use one
session per independently owned window or activity. Instances registered directly into DI remain
externally owned under the usual Microsoft DI rules; prefer factories for session-owned resources.
The channel service itself must use an unkeyed singleton factory or type registration, so it is
owned by exactly one session. Shared channel-service instances and non-singleton registrations fail.
Scoped services require an explicit DI scope; root resolution is validated.

Link view work to `session.Stopping`. When the window/activity permanently closes, await
`session.DisposeAsync()`. Before exiting the application, await `host.DisposeAsync()` to stop
all remaining sessions. Repeated disposal shares completion; new sessions and service access are
rejected once shutdown starts. Cleanup starts on the UI thread and cancels view work. Services
dispose in DI's reverse creation order, allowing plugins to send final stop requests; the host
also stops channels if an application service interrupts DI cleanup. Failures are reported after
cleanup is attempted for every session.
Keep the UI loop running until the awaited cleanup completes.

The host deliberately leaves close confirmation and application exit policy to the application.
For desktop close requests, cancel the initial close, await cleanup, then close/exit again with a
guard. The Currency sample demonstrates this and Android activity destruction. Do not dispose a
session merely because a control temporarily detaches or a mobile app goes into the background.
Browser tab destruction and mobile process termination cannot guarantee completion of asynchronous
cleanup; explicitly awaited application shutdown is the deterministic path.

Services start lazily. If resolution fails, dispose the session (or its host) to release services
already constructed. `AvaloniaChannelHost.Create(configureServices)` provides the same ownership
without AppBuilder integration for applications managing their own startup.

## Incoming UI handlers

```csharp
var channel = session.Channels.GetMethodChannel("ui", new MethodChannelOptions
{
    HandlerDispatcher = AvaloniaChannelDispatchers.UIThread
});
```

The same dispatcher works with `BasicMessageChannelOptions`. Handler starts are queued on the UI
thread; awaiting a remote response does not block the dispatcher. Cancellation before a queued
handler starts prevents its invocation; cancellation while it runs ends the waiting dispatch and
the handler must observe its captured cancellation token to stop its own work. Thread affinity
does not propagate through a handler's explicit `ConfigureAwait(false)`.

Use background handlers when UI access is unnecessary. Event consumers should dispatch their own
UI updates; this package does not imply event or native-view acceptance on every target.
