# Apple native integration

Swift implements the native bridge behind explicitly named Objective-C classes and protocols.
SDK-style .NET bindings expose `MCNativeBridge`, `MCRequestHandler`, `MCRequestContext`, `MCReply`,
and `MCNativeModule`. The Swift MethodChannel helpers stay native.

## Create a module

This complete Swift module exposes `echo` and a request to the managed `getToken` handler on
`example`. Build it in an adapter framework that imports `NativeChannelsNative`:

```swift
import Foundation
import NativeChannelsNative

@objc(MCExampleModule)
public final class ExampleModule: NSObject, NativeModule {
    public required override init() { super.init() }

    public func register(with bridge: NativeBridge) {
        let channel = MethodChannel("example", bridge: bridge)
        precondition(channel.setHandler(onMainThread: false) { call, result in
            switch call.method {
            case "echo": result.success(call.arguments)
            case "callback":
                do {
                    let requestId = try channel.invoke("getToken") { value, error in
                        if let error {
                            result.failure(error.code, message: error.message, details: error.details)
                        } else {
                            result.success(value)
                        }
                    }
                    result.onCancel { channel.cancel(requestId) }
                } catch {
                    result.failure("serialization_error", message: "Cannot encode callback", details: nil)
                }
            default: result.notImplemented()
            }
        })
    }
}
```

The registration precondition detects a duplicate native handler in this example. Each configured
module should own unique endpoint registrations. Registration installs handlers; initiate managed
requests only after the managed receiver is attached. Install the C# `getToken` handler on `example`
before invoking `callback`.

## Build and include the framework

`python3 eng/build-apple.py` builds the bridge and bundled sample adapter as XCFrameworks, with iOS
device arm64, simulator arm64/x64, and Catalyst arm64/x64 slices. It includes Swift modules and textual
interfaces for native adapters. Use [SampleModule.swift](../native/apple/Samples/SampleModule.swift)
and the build script as a reference when creating your own adapter framework.

Include your adapter in the MAUI application:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios' Or $([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'maccatalyst'">
  <NativeReference Include="Native/ExampleNative.xcframework">
    <Kind>Framework</Kind>
    <SmartLink>false</SmartLink>
  </NativeReference>
</ItemGroup>
```

Configure `MCExampleModule` and `ExampleNative` in `NativeModuleRegistration`. The transport loads the
framework from the app bundle before resolving the Objective-C class name. `SmartLink=false` retains
the registration facade; add the SDK's own compatible frameworks explicitly.

Frameworks remain loaded for the process lifetime because Objective-C class metadata can outlive a
session. Session disposal detaches callbacks and drains handler entry before releasing managed peers.

## Async work and UI dispatch

`setHandler(onMainThread: true)` enters a callback handler on the main thread. A callback can complete
later with `success`, `failure`, or `notImplemented`; duplicate replies are ignored.

`setAsyncHandler` starts a Swift Task and forwards its result. Use `MainActor.run` for UI work inside
that handler; the Task itself is not a promise of main-thread execution. Register `result.onCancel`
for callback-based SDK work. Async handler Tasks are cancelled automatically, but the SDK still needs
to cooperate with cancellation.

Native `channel.invoke` throws if encoding fails and returns a request ID when submitted. Retain
that ID and call `channel.cancel(id)` when the native owner ends or its own deadline expires.
Do not synchronously block a dispatch queue waiting for a remote response.

## Verify each target

The package-only sample checks calls in both directions, asynchronous completion, UI dispatch,
errors, cancellation, and handle cleanup. Use `eng/verify-ios-sample.sh` after a clean Release build.
Compile device and [Mac Catalyst](maccatalyst-integration.md) targets separately. Simulator execution
is not physical-device verification. See [building](building.md) and the [verification record](verification.md).

For value/reply contracts, use [BasicMessageChannel](basic-message-channels.md#swift). For independent
native subscriptions, use [EventChannel](event-channels.md#swift-producer) and register SDK cleanup
with each sink's `onTermination` callback. These Swift helpers ship in the same XCFramework and do
not require new method-specific C# bindings.
