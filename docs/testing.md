# Testing channel integrations

Use portable tests for application logic and native consumers for bridge, loading, and packaging
behavior. Passing one layer does not establish that the other works.

## Run a channel entirely in .NET

Reference `NativeChannels.Core` in a .NET 10 test or console project. This complete console example
creates two endpoints, registers a handler, and performs a typed call:

```csharp
using NativeChannels;

var pair = InMemoryTransport.CreatePair();
await using var client = new ChannelService(new BinaryMessenger(pair.Managed));
await using var server = new ChannelService(new BinaryMessenger(pair.Native));
using var incoming = server.GetMethodChannel("calculator");
using var registration = incoming.SetMethodCallHandler((call, cancellation) =>
{
    cancellation.ThrowIfCancellationRequested();
    if (call.Method != "add") throw new MethodNotImplementedException(call.Method);
    var sum = call.GetArgument<int>("a") + call.GetArgument<int>("b");
    return ValueTask.FromResult<object?>(sum);
});

using var outgoing = client.GetMethodChannel("calculator");
var result = await outgoing.InvokeAsync<int>("add", new Dictionary<string, object?>
{
    ["a"] = 10,
    ["b"] = 20
});
Console.WriteLine(result); // 30
```

The services own their messengers and transports. Handles and registrations dispose first because
of their declaration order. This exercises the actual managed messenger and codec without MAUI.

## Test application behavior

Cover successful values and null, unknown methods, structured errors, cancellation, and feature
cleanup. Retain handler tokens until the test finishes. Use a completion signal when a test must wait
for handler entry instead of relying on a short sleep. For deadline races, inject a controlled
`TimeProvider` into `BinaryMessenger`; a real 1 ms deadline can expire before a slow runner dispatches
anything.

The repository's portable suite runs with:

```sh
bash eng/dotnet.sh test -c Release
```

For repeated lifecycle stress and optional benchmarks, see [performance](performance.md).
Package metadata regression tests use only Python's standard library:

```sh
python3 -m unittest discover -s tests/packaging
```

## Verify the native boundary

Use the [package-only sample](../samples/NativeChannels.Sample/README.md) after changing a bridge,
binding, native dependency, or packaging rule. Restore into a fresh package cache and rebuild clean
consumer outputs. The scripts verify source mapping, archive hashes, and absence of library project references.

Run Release consumers on Android and iOS simulators to exercise shrinking/linking and actual framework
loading. Compile device/Catalyst targets separately and record runtime execution separately. Shared
C#, Kotlin, and Swift protocol fixtures live in `tests/fixtures/values.json`.

See [building](building.md) for native test commands and [verification](verification.md) for recorded
results and CI evidence.

## Verify a reusable plugin

The [currency sample](../samples/Currency.Sample/README.md) references `Plugin.Maui.Currency` and
its explicit `NativeChannels.Maui` host through packages. The plugin itself has no MAUI dependency;
the Windows package tests verify its dependency graph as well as real native currency calls.
Its AAR and XCFramework arrive through the package's `buildTransitive` imports. Run its fresh-cache
restore and Release runtime checks after changing the plugin, its native modules, or packaging.
`plugins/Plugin.Maui.Currency/eng/inspect-package.py` checks the native classes, preservation rules,
framework slices, dependencies, and import paths inside the actual archive.

The [Avalonia Android proof](../samples/Avalonia/Currency/Currency.Android/README.md) and
[iOS proof](../samples/Avalonia/Currency/Currency.iOS/README.md) check host independence. Each reusable workflow
runs after its platform's framework verification, reuses those packages, and builds Currency once.
MAUI and Avalonia restore separately and execute the shared scenarios in the same emulator or
simulator. Package hashes must match and Avalonia's dependency graph must exclude MAUI. Each runtime
proof is mandatory when its platform is selected, independently of the framework sample's
Compile/Runtime depth. A failure blocks the framework result. Logs and hashes are retained as
`shared-hosting-android` and `shared-hosting-ios`; iOS also retains app screenshots. Simulator success
does not establish physical-device support.

The [Windows proof](../samples/Avalonia/Currency/Currency.Desktop/Windows.md) compares the three shared
archives in separate fresh MAUI and Avalonia caches and rejects mobile bindings or WinUI dependencies
in Avalonia. Its required CI job builds both hosts for x64 and ARM64 and runs the native Currency API
tests on the x64 runner. Desktop application execution is separate: run
`./eng/verify-shared-hosting-windows.ps1` on Windows to execute both applications and check Avalonia's
normal window/session shutdown. `-BuildOnly` never creates a runtime-success record.

The portable solution includes Currency facade and registration tests, alongside core channel tests.
Shared registration tests exercise lazy session creation, independent providers, captured settings,
duplicate and invalid configuration, host dispatch and owned disposal. They compile the production
C# source without MAUI workloads; native app checks remain necessary to establish native loading
and real platform results.

The message/event tests cover null versus empty payloads, strict UTF-8, bidirectional replies,
registration ownership, independent enumerations, bounded overflow policies, buffered terminal errors,
service/handle cancellation, and cleanup after typed conversion failures. Native tests additionally
exercise the producer queue and its one-shot termination cleanup. The MAUI sample repeats the
message/stream contract through actual packaged Kotlin and Swift adapters.

## Browser Currency

The [Browser Currency proof](currency-avalonia-browser.md) uses packaged ES modules, a fresh
WebAssembly consumer and real Chromium/Firefox execution. JavaScript tests run with
`node --test tests/NativeChannels.Browser.Tests/*.test.mjs`. The browser workflow publishes Release,
checks archive and static-asset hashes, then verifies Currency, bidirectional callbacks, binary/value
messages, cancellation, timeout, input, reload and disposal. See its guide for the full commands.

## Linux Currency

The [Avalonia Linux Currency proof](currency-avalonia-linux.md) adds real system-ICU tests on Linux,
fresh-package X11 application checks, and normal window-close disposal. Run
`bash eng/dotnet.sh test tests/NativeChannels.Linux.Tests -c Release` on Linux; this separate suite
requires system ICU and is intentionally outside the cross-platform portable solution.
The Linux workflow also runs the existing portable and packaging suites. Xvfb execution verifies
the real X11 backend, but does not establish Wayland or physical desktop input coverage.

## Native view lifecycle

Portable tests exercise view-channel ownership, cooperative cancellation, isolated instances, and
registration cleanup. The package-only sample additionally creates actual native views, checks
creation parameters and bidirectional clicks, unloads/reloads a view, rejects background reconnect,
and verifies fresh instance IDs and stale-channel failure. Both Android and Apple verification
scripts require the `native views:` result. Native UI and framework loading still require platform execution.

## Avalonia host foundation

`tests/NativeChannels.Avalonia.Tests` runs against Avalonia 12.1.4 headless UI dispatch with xUnit v3.
It tests application registration, independent providers, repeated asynchronous disposal, cancellation,
startup/cleanup failures, final native stop requests, session recreation and reentrant UI handlers.
Run `bash eng/dotnet.sh test tests/NativeChannels.Avalonia.Tests -c Release`; the project is also in the
portable solutions. Platform consumers must restore `NativeChannels.Avalonia` from the same local
feed, with its archive hash checked separately from the packages shared with MAUI.

Use `bash eng/pack-avalonia.sh` after building a platform/Core feed. The host itself restores the
exact Core archive from that feed through a fresh package cache. Linux/browser pack scripts and the
shared-hosting workflows include this step. Runtime evidence remains scoped to its recorded source.
