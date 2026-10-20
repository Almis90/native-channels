# Uno 0.3 preparation

Status on October 20, 2026: **experimental development; not release-ready**.
The optional Uno host, shared messaging plugins, five sample apps and desktop
build automation exist. Full six-platform runtime and native-view acceptance
remain blocked. No 0.3 release, tag or publication has been performed by this work.

## Package and version boundaries

The repository release version remains `0.2.0-preview.1`. The Uno diagnostic
feed uses `0.3.0-preview.6` to identify local experimental packages; that number
is not a published version or an approved release candidate. Do not install
these packages from a public feed expecting the behavior described here.

| Package | Responsibility |
| --- | --- |
| `NativeChannels.Core` | Framework-independent messaging, codecs and channel lifetimes |
| `NativeChannels` | Platform transports and shared native module registration |
| `NativeChannels.Uno` | Optional Uno startup, per-window sessions, UI dispatch and experimental view lifecycle |
| `Plugin.Currency`, `Plugin.AudioStreamer`, `Plugin.VolumeMonitor` | Shared messaging plugins; no dependency on a UI host |
| `Plugin.Uno.PdfViewer` | Uno PDF control with a Windows.Data.Pdf child-window renderer; custom factories remain supported |

The desktop transport selects Windows, Linux or macOS at runtime. The macOS
plain-.NET bridge stays in the transport/native layer. It does not belong in
the Uno host. MAUI and Avalonia hosts remain optional; adding Uno does not require
renaming or duplicating the three shared messaging plugins.

Uno SDK 6.7.30 resolves runtime 6.7.135. The current samples use Skia and .NET SDK
10.0.401. See [host setup](uno-host.md), [plugin registration](uno-plugins.md),
and the [sample instructions](../samples/Uno/README.md).

## Current evidence

| Platform | Executed evidence | Still required |
| --- | --- | --- |
| Windows | Five package-only apps pass bounded smoke checks; real Currency, silent audio and volume plugins execute. Corrected button adapters pass native checks; Windows PDF painting, page/zoom, invalid-input and cleanup checks also pass | Visible review and physical input, broader PDF/document/lifecycle coverage and audible playback checks |
| macOS | Earlier ARM64 transport and native contract probe; x64 native libraries cross-compile | Full five-app execution, shared plugin runtime, public native-view hosting path and PDF integration |
| Linux | Earlier portable transport selection/method/disposal test in an Ubuntu VM | Five Uno app executions, native plugin packaging and view/PDF integration |
| Android | Sample heads exist | Uno package integration, native method/view probes and five-app device/emulator execution |
| iOS | Sample heads exist | Uno package integration, native method/view probes and five-app simulator/device execution |
| Browser | Sample heads exist | Uno transport/assets integration, DOM/PDF hosting and five-app browser execution |

The latest local step-6 Windows run rebuilt all seven diagnostic packages,
audited their metadata and source commit, and verified that every local package
restored by each app matched that run's feed byte-for-byte. Local package caches
were fresh; third-party dependencies were reused. All five builds and smoke
checks passed. Interop uses managed loopback for its automated channel/lifecycle
checks; PDF uses a managed protocol fixture. Neither result proves native rendering.

That run used package source `b24c6d623e53d806f1c552c7be67208e12112518`
and the then-uncommitted runner subsequently committed as `319c672`. Its source
digest and dirty-tree status are recorded in
`artifacts/local-samples/uno/windows/20261019-232553-2e12f0f0/summary.json`.
These local artifacts are not a hosted CI result or clean release-candidate proof.
The runner tests passed 52 checks; workflow lint and documentation validation
also passed during step 6. Earlier probe scope is recorded in
[Uno feasibility](uno-feasibility.md).

## Reproduce and interpret results

On Windows, from the repository root:

```powershell
python eng/test-samples.py --framework uno --platform windows --dotnet .tools/dotnet/dotnet.exe
```

Use `--reuse-external-cache artifacts/uno-feasibility/cache` to reuse third-party
dependencies while retaining a fresh cache for local packages. Use `--require-clean`
when collecting candidate evidence. `--sample Currency` selects one app; omit it
for all five. Each invocation writes its own package feed, hashes, restore assets,
build/runtime logs, source identity and summary under `artifacts/local-samples/uno`.

`--build-only` compiles on Windows, Linux or macOS and records runtime checks as
`not-run`. The desktop CI workflow defines these three build jobs; they have not
been executed as part of the local work recorded here. CI configuration is not
evidence that those jobs passed.

Only Windows has a smoke execution path in the unified Uno runner. Unsupported
runtime requests produce blocked evidence and exit 2. Even successful Windows
smoke checks record full acceptance as `blocked`. Add `--require-acceptance` to
make incomplete acceptance fail the command. A zero smoke/build exit code must
not be used as a release approval.

## Remaining release gates

- Complete visible Windows review and physical input verification. The corrected
  adapter passes automated two-view parenting, layout, focus, direct input messages,
  reconnect, stale channel invalidation and cleanup checks; see the
  [Windows probe correction](uno-feasibility.md#windows-native-view-probe).
- Establish a public supported macOS native embedding path. The pinned Uno
  release exposes its arbitrary native-element wrapper internally; reflection,
  separate windows and rendered-image substitutes do not meet the planned contract.
- Supply and verify Uno PDF adapters for the other five platforms. Windows native
  painting and lifecycle checks now pass; physical input and broader visual/document
  review remain outstanding. Protocol fixtures alone are insufficient.
- Finish Android, iOS and browser transport/package integration and validate
  desktop native assets for Linux and both macOS architectures.
- Execute all five apps across all six platforms against one intended package
  candidate, recording backend, architecture, package hashes and lifecycle results.
  Keep physical devices, additional architectures and manual observations explicit.
- Extend the distribution/API compatibility and publication manifests for Uno,
  then validate the exact combined archives from clean, unchanged source. The
  existing 0.1/0.2 publication path has not been extended or approved for 0.3.
- After acceptance, update release versions and dependency pins together, finalize
  release notes, and follow the [release checklist](release-checklist.md). Publication
  and public-feed consumer checks are separate steps.

Documentation and release notes are prepared here; they do not close the earlier
milestone gates. The full 0.3 milestone remains incomplete.
