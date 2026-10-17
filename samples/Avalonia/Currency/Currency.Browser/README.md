# Currency in Avalonia Browser

This WebAssembly proof runs the shared Currency view and C# facade against a packaged JavaScript
adapter. The adapter uses `Intl.supportedValuesOf('currency')` to validate codes and
`Intl.NumberFormat(...).formatToParts(0)` to obtain a localized symbol. Locale comes from the
browser's language preferences. There is no embedded currency table or managed lookup fallback.

## Build and run

Use the repository-pinned .NET SDK, Node.js 24, and the `wasm-tools` workload. On Ubuntu 24.04 the
WebAssembly linker also needs `libatomic1`. Run from the repository root:

```bash
bash eng/bootstrap.sh
bash eng/dotnet.sh workload install wasm-tools --skip-manifest-update
# Ubuntu only, if needed: sudo apt-get install -y libatomic1
bash eng/pack-browser.sh
bash eng/restore-currency-avalonia.sh browser
bash eng/dotnet.sh publish samples/Avalonia/Currency/Currency.Browser -c Release --no-restore
node eng/serve-browser.mjs samples/Avalonia/Currency/Currency.Browser/bin/Release/net10.0-browser/publish/wwwroot
```

Open `http://127.0.0.1:8085/currency/`. Preserve previous platform packages before building:
`pack-browser.sh` requires an empty `artifacts/packages` feed. It builds only `NativeChannels.Core`,
`NativeChannels`, and `Plugin.Maui.Currency`. The latter two use `net10.0-browser`; the Core package
stays portable. These are scoped development archives with existing preview version numbers, not
a combined release feed or newly published NuGet versions.

The sample references the shared UI project and consumes all library assemblies from a fresh
NuGet cache. Both packages carry their own ES modules and `buildTransitive` content items. Publish
places them under `_content/NativeChannels/` and `_content/Plugin.Maui.Currency/` automatically.
Do not copy adapters out of the source checkout. A production static host must serve `.mjs` as
JavaScript and `.wasm` as `application/wasm`; use HTTP(S), not `file://`. Keep the packaged module
URLs relative to the document base, including when deploying under a subpath. Revalidate these
unfingerprinted modules on deployment so an older bridge is not cached with newer assemblies.

## Browser adapter contract

Set `NativeModuleRegistration.BrowserModule` to an ES module URL relative to `document.baseURI`.
Its exported `register(context)` may be asynchronous and must return a disposer. Registration is
per transport. A failed registration rolls back earlier adapters; disposal releases registrations,
pending replies, and the managed callback reference. Dispose newly allocated resources within
`register` if it throws before returning its disposer.

```javascript
export function register(context) {
    return context.registerMethodHandler('example.greeting', (method, argument) => {
        if (method !== 'hello') return context.notImplemented;
        return `Hello, ${argument}`;
    });
}
```

`registerBinaryHandler(name, (Uint8Array, AbortSignal) => Promise<Uint8Array>)` handles raw payloads.
`context.send(name, bytes, signal)` calls managed handlers asynchronously and returns
`{ status, payload }`. Status values match `TransportStatus`. Buffers are copied through a base64
envelope; the bridge does not interpret their codec. The method helper implements tagged JSON v1,
including `BigInt` for Int64, nested values, bytes and structured `context.ChannelError` errors.
JavaScript integer Numbers in the Int32 range encode as Int32; other finite Numbers encode
as doubles. Use BigInt for wide integers. Default bounds are 16 MiB, 256 UTF-8 bytes per name and
64 levels of value nesting. Smaller managed limits remain enforced.

This implementation uses the main browser thread and asynchronous .NET `JSImport` callbacks.
WebAssembly worker/thread hosting is not covered. Package module code is trusted application code;
the channel boundary is not a JavaScript sandbox.

The sample exposes `currencyApp.CloseAsync()` for a host to await before navigation. `pagehide`
starts best-effort cleanup, and a BFCache restore reloads the app to create a fresh session.
Browsers cannot guarantee completion of asynchronous work during tab destruction. Verification
explicitly awaits disposal, checks repeated disposal, and reloads the application.

## Verification

```bash
node --test tests/NativeChannels.Browser.Tests/*.test.mjs
npm ci --prefix tests/NativeChannels.Browser.Tests
node tests/NativeChannels.Browser.Tests/node_modules/playwright/cli.js install --with-deps chromium firefox
python3 eng/prepare-browser-proof.py
node eng/verify-currency-browser.mjs
```

The preparation step validates local package origin and archive hashes, rejects MAUI/mobile/WinUI
dependencies, compares published JavaScript with the actual archives, and fingerprints every
published file. The browser runner checks that inventory before serving the app under `/currency/`.
It records browser versions, console output, screenshots, source and input-manifest hashes, the
shared Currency scenarios, managed/JavaScript transport checks, real mouse/keyboard lookups and
error recovery, reload and orderly disposal.
Evidence is written under `artifacts/shared-hosting-browser/`.

`BROWSER_CHROMIUM_CHANNEL=chrome` or `msedge` selects an installed Chromium-family browser;
otherwise the runner uses Playwright Chromium. `PLAYWRIGHT_BROWSERS_PATH` can isolate downloaded
browsers. See the [verification record](../../../../docs/verification.md#avalonia-in-the-browser)
for configurations actually executed. The dedicated
[browser workflow](../../../../.github/workflows/shared-hosting-browser.yml) reproduces the
package build and Chromium/Firefox checks on Ubuntu.

## Scope

Currency needs modern browser `Intl` support and no permissions, network service or OS plugin.
The browser's own currency data determines which codes it recognizes. Missing `supportedValuesOf`
is reported as initialization failure. Browser engines and locale data can produce different symbols.

The proof covers Currency methods plus generic binary/value roundtrips, exact Int64 values,
asynchronous callbacks in both directions, structured errors, cancellation, timeouts, isolation,
failed initialization, and disposal. DOM/native views, JavaScript event-stream helpers, the other
reference plugins, worker threads, Safari/WebKit, mobile browsers, BFCache acceptance and a general
Avalonia host package remain outside this proof. Full milestone 0.2 acceptance is tracked in the
[roadmap](../../../../docs/roadmap.md#avalonia-integration).
