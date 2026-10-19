# Preparing 0.2.0-preview.1 locally

The combined candidate from `4f1079b` passed local acceptance on October 19:
all five samples across six Avalonia targets and four MAUI targets, with both
Linux PDF heads (51 executions). See [verification](verification.md#local-02-candidate)
for exact package hashes, environments and limitations. The candidate is unpublished;
these commands reproduce the local preparation and acceptance process.

## Candidate scope

The 0.2 framework candidate contains six packages and their managed symbols:
`NativeChannels.Core`, `NativeChannels`, `NativeChannels.Maui`,
`NativeChannels.Avalonia`, `NativeChannels.Native.Android`, and
`NativeChannels.Native.Apple`. Reference plugins remain local examples, outside
the framework release. Uno remains planned for 0.3.

Local coverage includes Windows x64, Android x86_64 emulation, Apple ARM64 desktop
and iOS simulation, Linux x64/X11 (Qt and GTK PDF), and Chromium/WebAssembly.
Physical devices, Windows ARM64, Apple x64 execution, native Wayland embedding,
additional browser engines, accessibility, long-running lifecycle/heap tests and
third-party SDK integrations are not established by this matrix. Audible audio,
PDF visual fidelity and hardware volume buttons require manual checks. Browser
volume events concern page-owned audio, not system volume; its single-threaded
runtime cannot verify background-thread rejection. Linux null-sink runs exercise
buffering without proving audible output. Keep the Mac fully awake during Apple
simulator audio checks, including an open lid when needed.

## Prepare clean-source platform packages

Commit code, package metadata and documentation first. Use that exact commit on
Windows, Linux and the Mac with the SDK pinned in `global.json`. Keep the source
clean throughout packaging and execution. Run one build at a time per checkout;
separate hosts may build concurrently. Never stamp a new source commit on old
binaries or use earlier development feeds as final-package evidence.

Generate ten platform feeds using the [local sample runner](local-sample-tests.md):

```sh
python3 eng/test-samples.py --framework avalonia --platform windows --packages-only --require-clean
```

Repeat for Avalonia `android`, `ios`, `macos`, `linux`, `browser`, and MAUI
`windows`, `android`, `ios`, `maccatalyst` on the appropriate hosts. On Windows
use `python` if needed. `--packages-only` audits and hashes the archives without
building apps. `--reuse-external-cache` can conserve disk space; framework and
reference-plugin archives always use isolated caches and feeds.

Transfer each `build/manifest.json` and adjacent `build/packages/` directory to
the assembly host without changing their bytes. Checkout paths may differ.
The assembler takes portable Core/Avalonia from the Avalonia Windows feed,
platform assemblies from the respective Avalonia feeds (MAUI for Catalyst),
and MAUI assemblies from the four MAUI feeds.

## Assemble and compare APIs

Supply all ten build manifests to one invocation:

```sh
python3 eng/preview_candidate.py --source-commit <full-commit-SHA> \
  --manifest <avalonia-windows/build/manifest.json> \
  --manifest <avalonia-android/build/manifest.json> \
  --manifest <avalonia-ios/build/manifest.json> \
  --manifest <avalonia-macos/build/manifest.json> \
  --manifest <avalonia-linux/build/manifest.json> \
  --manifest <avalonia-browser/build/manifest.json> \
  --manifest <maui-windows/build/manifest.json> \
  --manifest <maui-android/build/manifest.json> \
  --manifest <maui-ios/build/manifest.json> \
  --manifest <maui-maccatalyst/build/manifest.json> \
  --output artifacts/preview-02/candidate
```

Assembly checks clean source, hashes, scoped metadata, framework/dependency
coverage, browser bridge assets and symbols. Conflicting shared payloads fail.
The output has twelve archives and `preview-manifest.json`. Assembly is not acceptance.

Restore the pinned tools, then check the five existing package APIs against the
hash-pinned baseline. Avalonia is new and has no published package API baseline.

```sh
.tools/dotnet/dotnet tool restore
python3 eng/verify-api-compatibility.py --scope combined \
  --packages artifacts/preview-02/candidate \
  --output artifacts/preview-02/api-compatibility.json
```

On Windows use `.tools/dotnet/dotnet.exe`; the API checker also accepts `--dotnet`.
Do not suppress compatibility errors or substitute a partial scope for this check.

## Execute exact-package consumers

Copy the whole candidate directory unchanged to each execution host. Rebuild and
run all ten suites using `--framework-packages`, for example:

```sh
python3 eng/test-samples.py --framework avalonia --platform windows \
  --framework-packages artifacts/preview-02/candidate
python3 eng/test-samples.py --framework maui --platform ios --device <simulator-UDID> \
  --framework-packages artifacts/preview-02/candidate
```

This mode checks candidate hashes/source, consumes those exact framework archives,
builds the reference plugins from the same source and checks consumer assets.
It never repacks the framework. Summaries embed the candidate manifest. Earlier
passing suites without this option cannot establish candidate acceptance.

Collect the ten summaries and combined API report. Invoke the acceptance gate
with `--summary` repeated for every suite:

```sh
python3 eng/preview_candidate.py --accept artifacts/preview-02/candidate \
  --source-commit <full-commit-SHA> \
  --api-report artifacts/preview-02/api-compatibility.json \
  --summary <avalonia-windows/summary.json> \
  --summary <avalonia-android/summary.json> \
  --summary <avalonia-ios/summary.json> \
  --summary <avalonia-macos/summary.json> \
  --summary <avalonia-linux/summary.json> \
  --summary <avalonia-browser/summary.json> \
  --summary <maui-windows/summary.json> \
  --summary <maui-android/summary.json> \
  --summary <maui-ios/summary.json> \
  --summary <maui-maccatalyst/summary.json> \
  --output artifacts/preview-02/local-acceptance.json
```

The gate rejects missing/duplicate suites, failed samples, changed archives,
partial API reports, dirty/different source and reports for other candidate bytes.
Linux must include both PDF heads. Retain complete logs, audits, input hashes,
package manifests and API output alongside the summaries.

## Publication remains separate

These preparation commands invoke no workflows and do not tag, push or publish.
The [Trusted Publishing path](publishing.md#trusted-preview) stages these exact
archives and evidence, then runs only the publishing workflow after explicit
authorization. Tests remain local. A [direct local publisher](publishing.md#local-preview)
is also available with an API key. Neither path rebuilds the accepted archives.
Fresh NuGet.org restores, symbol status and consumer checks remain necessary after publication.
