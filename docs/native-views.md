# Native views

`PlatformChannelView` hosts an explicitly registered Kotlin/Java or Swift/Objective-C view inside
a MAUI layout. Each connection has its own native instance, bridge, and MethodChannel. Two views
using the same factory remain independent.

Windows supports WinUI elements through `WindowsFactory` and
`WindowsNativeView`. See [Windows integration](windows-integration.md#embed-a-winui-view).

For a practical integration, see [PdfViewer](pdf-viewer.md): an embedded PDFKit/PdfRenderer
surface with navigation, zoom, and native callbacks. The button below is the small lifecycle
fixture used by the framework's verification sample.

## Register a factory

Include the adapter AAR or XCFramework just as you would for a [native module](getting-started.md).
`UseNativeChannels` registers the MAUI handler automatically. Factory registration is separate from
`Modules`; a view factory is created when a view connects.

```csharp
builder.UseNativeChannels(options =>
{
    options.ViewFactories.Add(new NativeViewFactoryRegistration(
        "sample.button",
        "dev.nativechannels.sample.ButtonViewFactory",
        "MCButtonViewFactory",
        "NativeChannelsSample"));
});
```

The factory name is case-sensitive and must be unique. Use the full Android class name and the
explicit Objective-C class name. Apple framework names omit `.framework`. Missing registrations,
classes, or framework assets fail with an exception; no empty fallback view is supplied.

## Use the view

```csharp
var nativeButton = new PlatformChannelView
{
    FactoryName = "sample.button",
    HeightRequest = 72,
    CreationParameters = new Dictionary<string, object?> { ["title"] = "Tap me" }
};
nativeButton.Connected += (_, _) =>
{
    nativeButton.Channel!.SetMethodCallHandler((call, token) =>
    {
        if (call.Method != "clicked")
            throw new MethodNotImplementedException();
        // This handler enters on the MAUI UI thread.
        statusLabel.Text = $"Native tap {call.GetArgument<int>("count")}";
        return ValueTask.FromResult<object?>("managed acknowledgement");
    });
};
layout.Children.Add(nativeButton);
```

`InstanceId` and `Channel` are null while disconnected. Install handlers in `Connected`, since every
reconnection supplies a new channel. Once connected, invoke the adapter's methods through
`nativeButton.Channel`, for example `await nativeButton.Channel!.InvokeAsync("setTitle", "Updated")`.
The sample's method names are adapter-specific, not built-in view operations.

Creation parameters use `JsonMessageCodec` and the application's configured serializer metadata
and limits. DTOs need source-generated metadata for trimming/AOT. Creation parameters are encoded
once per connection. Replacing either configuration property while connected throws. To change
configuration, await `DisconnectAsync()`, update the properties, then call `Reconnect()`.
Mutating an existing parameters collection does not update the native view; use a method call.
Set a MAUI size or layout constraint explicitly; native intrinsic-size measurement is not provided.

## Implement the native factory

On Android, implement `NativeViewFactory` with a public parameterless constructor. Its `create`
method receives an Android context, `NativeBridge`, instance ID, channel name, and copied argument
bytes. Return a `NativeViewInstance` whose `getView()` returns one new unattached Android view.
Its `close()` removes listeners and releases adapter-owned resources.

On Apple, export an `NSObject` subclass with an explicit `@objc` name and implement
`NativeViewFactory` (`MCNativeViewFactory`). `createView` receives the bridge, instance ID, channel
name, and argument `Data`; return a `NativeViewInstance` (`MCNativeViewInstance`) with a stable
`UIView` and `close()`. The bridge loads the registered adapter framework before looking up its class.

Decode arguments with the native `JsonMessageCodec`, and construct a native `MethodChannel` using
exactly the supplied name and bridge. Configure its handler with `onMainThread = true` (Kotlin) or
`onMainThread: true` (Swift) before touching UI. Do not invoke managed callbacks during factory
construction; install them after the managed `Connected` event has had a chance to register a handler.

The working factories show creation parameters, updating a native button, click callbacks, and cleanup:

- [Android ButtonViewFactory](../native/android/sample/src/main/java/dev/nativechannels/sample/ButtonViewFactory.kt)
- [Apple ButtonViewFactory](../native/apple/Samples/ButtonViewFactory.swift)
- [Managed lifecycle verification](../samples/NativeChannels.Sample/ViewVerification.cs)

Only generic view-host operations are bound to C#. Keep your adapter class reachable: the Android
bridge ships a consumer rule for `NativeViewFactory` implementors, and Apple factories need their
explicit Objective-C names in the packaged framework.

## Lifecycle and threading

Creation, attachment, disconnection, and reconnection require the UI thread. Managed incoming view
handlers enter on that thread. Native UI handlers must opt into the native main-thread dispatcher;
channel calls never make arbitrary native SDK operations thread-safe automatically.

Unloading the MAUI view or disconnecting its handler invalidates the channel immediately, removes
native content, calls the adapter's `close()`, and drains its isolated bridge. Outstanding calls fail
with `ObjectDisposedException`. Owned managed registrations are removed and late responses are
ignored. Cleanup of one view cannot dispose another view's bridge or the app-wide channel service.
The native adapter must cancel its own timers, observers, and SDK operations in `close()`.

Loading the view again creates a new instance. `Reconnect()` also recreates it immediately when a
MAUI handler is available. Both operations assign a fresh instance ID and channel name; cached old
channels remain disposed. `DisconnectAsync()` permits deterministic cleanup while retaining the
MAUI wrapper. A later MAUI load or explicit `Reconnect()` connects it again.

Callers own neither the exposed channel nor its bridge. Let the view manage their lifetime; do not
cache the channel across connection changes. The view does not persist native state across detach,
activity recreation, or process death. Backgrounding alone is not a guarantee of disconnection.

## Verification

The package-only sample hosts two native buttons and exercises isolated state, creation parameters,
bidirectional callbacks, UI dispatch, reconnect, pending-call cancellation, and stale-channel rejection.
Run it using the [sample commands](sample.md). The [verification record](verification.md) separates
compiled targets from actual execution and physical-device coverage.
