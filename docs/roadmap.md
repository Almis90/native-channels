# Roadmap

NativeChannels begins with the existing channel implementation and MAUI integration for Android,
iOS, Mac Catalyst, and Windows. The next step is to make native integrations reusable across UI
frameworks, while keeping platform lifecycle and native view hosting explicit.

| Milestone | Outcome | Acceptance |
| --- | --- | --- |
| **0.1 — NativeChannels preview** | A reliable four-platform foundation | Verify packages in separate consumers; finish the remaining platform matrix and document limitations |
| **0.2 — Shared hosting and Avalonia proof** | Reuse a native plugin across application frameworks | Extract framework-neutral registration and dispatch contracts; run the same Currency package in MAUI and Avalonia Android apps |
| **0.3 — Avalonia integration** | A supported second application host | Startup, disposal, dispatch, message and event examples; define and verify each native-view and desktop platform integration |
| **1.0 — Stable API and distribution** | Public contracts ready for long-term use | Compatibility checks, supported-platform acceptance, lifecycle and performance evidence, and reviewed distribution |

## Initial preview

Implemented features include method calls, binary/value messages, independent event subscriptions,
and native views with instance channels. The four reference plugins demonstrate these contracts.

Remaining acceptance work includes fresh package-only release validation, Windows ARM64
execution, broader physical-device and audio-route checks, and long-running native lifecycle tests.
The [verification record](verification.md) distinguishes implemented, compiled, and executed behavior.

## Shared hosting

Keep `NativeChannels.Core` independent of application frameworks. Move shared module configuration
and registration out of the MAUI integration where appropriate. Framework hosts supply dispatchers,
startup/shutdown integration, and native view containers.

The first proof is a single Currency plugin consumed through packages by both MAUI and Avalonia on
Android. Extend that proof to messages and event streams before expanding native view hosting.
Avalonia support is planned and is not yet implemented.

## Platform expansion

Treat each host/platform combination as an explicit support decision. Mac Catalyst and Avalonia's
macOS desktop host use different UI and packaging models. Define the AppKit bridge and packaging
requirements before promising desktop macOS support. Windows WinUI views likewise require a host
adapter before they can be embedded in another UI framework.

## Stable release

Use the [release checklist](release-checklist.md). Package publication and release creation remain
separate actions; completing a milestone does not publish to NuGet.org.
