---
layout: home
title: Native integrations for .NET MAUI
description: Call Kotlin and Swift from C#, receive native events, and embed native views through a reusable bridge for .NET MAUI.
hero:
  name: NativeChannels
  text: Native APIs. One reusable bridge.
  tagline: Connect C# with Kotlin and Swift. Build native integrations with asynchronous calls, binary messages, event streams, and native views.
  image:
    src: /logo.svg
    alt: NativeChannels bridge connecting two endpoints
  actions:
    - theme: brand
      text: Get started
      link: /getting-started
    - theme: alt
      text: Explore examples
      link: /examples
features:
  - title: Keep native code native
    details: Use the SDK's Kotlin or Swift APIs behind a generic bridge. Expose a typed C# service without binding every SDK method.
    link: /architecture
    linkText: Understand the architecture
  - title: Choose your contract
    details: Named operations, binary chunks, independent subscriptions, or embedded native UI. Use the same bridge across your integration.
    link: /choosing-a-channel
    linkText: Choose a channel
  - title: Ship a reusable plugin
    details: Package your C# API and native adapters together. Verify the integration from a separate MAUI application with package references.
    link: /plugin-authoring
    linkText: Build a plugin
---

<div class="home-content">

<div class="home-status">
<span class="status-label">PREVIEW</span>

**Native calls, messages, streams, and views**

MAUI integration for Android, iOS, Mac Catalyst, and Windows. Build using the local package feed;
packages are not yet on NuGet.org.
[Platform verification](verification.md) · [Roadmap](roadmap.md)

</div>

## A small API for a native round trip

Register your native adapter once, then inject `IChannelService` into a service or view model:

```csharp
using var channel = channels.GetMethodChannel("sample");
string? reply = await channel.InvokeAsync<string>("echo", "Hello from .NET");
```

The bundled Kotlin and Swift adapters return the same string. Native code can also call a managed
handler and await its result. The [getting-started guide](getting-started.md) includes package setup,
module registration, and the complete application service.

## Four contracts, practical examples

| Build | With | Explore |
| --- | --- | --- |
| Native function calls with typed results | MethodChannel | [Currency symbol lookup](currency.md) |
| Binary data transfer with acknowledgements | BasicMessageChannel | [Native audio playback](audio-streamer.md) |
| Live observations with subscription cleanup | EventChannel | [System volume monitor](volume-monitor.md) |
| Embedded native UI with instance isolation | PlatformChannelView | [Native PDF viewer](pdf-viewer.md) |

Each reference plugin has its own MAUI sample and local package workflow.
See [examples](examples.md) for setup, what each one teaches, and platform limitations.

## Built around explicit lifetimes

Channel handles own their requests and registrations. Event enumerations own their subscriptions.
Native view connections own their instances and channels. Cancellation, timeouts, errors, and UI
dispatch are part of the contract.

[Learn channel ownership](lifecycle.md) · [Configure requests](configuration.md) ·
[Handle failures](error-handling.md)

## Find your next step

- **Application developers:** [make a first call](getting-started.md), then [choose a channel](choosing-a-channel.md).
- **Plugin authors:** [design and package an integration](plugin-authoring.md), starting from the Currency example.
- **Contributors:** [build from source](building.md), [run tests](testing.md), and review [release checklist](release-checklist.md).

Android, iOS, and Mac Catalyst builds are supported. Execution coverage and physical-device gaps are
recorded in the [verification matrix](verification.md). Browse the [guide index](overview.md) for the full documentation.

</div>
