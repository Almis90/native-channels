# Release checklist

For local 0.2 preview preparation, use the [candidate process](preview-02.md).
It covers six framework packages, including Avalonia, and all ten host/target
sample suites. It does not invoke or replace the existing publication workflows,
which still require adaptation for the expanded 0.2 package set.

Before distribution, require the [API compatibility gate](api-compatibility.md) against the published
baseline. The final combined packages must pass; a local mobile-only comparison is partial evidence.

Use these checks before tagging or distributing NativeChannels. Record the exact source revision,
package hashes, and platform results in [verification](verification.md). Compilation and runtime
execution are separate evidence. No workflow publishes packages automatically.

## Implemented checks

- Repeatable portable stress tests for reentrant requests, handler ownership, concurrent disposal,
  independent subscriptions, and view connection lifetimes.
- A regression test for throwing cancellation callbacks during in-memory transport disposal.
- A separate benchmark project for codec throughput/allocations and managed channel round trips.
- Repeated native view reconnect verification in the real package-only MAUI sample.
- Opt-in real-platform round-trip measurements and a paced native lifecycle/memory soak, with raw
  measurements, package hashes and a separate manual runtime workflow.
- Package metadata and source-revision checks with SHA-256 manifests, alongside existing native-asset inspections.
- Separate manual stress and benchmark workflows; ordinary CI retains one bounded pass of the portable stress tests.

See [performance](performance.md) for commands and measurement limits and
[verification](verification.md#automated-checks) for executed results.

## Distribution audit

After packing from a clean checkout, audit the exact packages intended for distribution:

```sh
python3 eng/package_audit.py artifacts/packages/*.nupkg --source-commit "$(git rev-parse HEAD)"
```

The audit rejects duplicate or unsafe ZIP entries, missing managed assemblies, mismatched package
identities, missing MIT license metadata, missing README content, and missing Git source metadata.
The optional source-commit argument also rejects packages built from another revision. CI supplies it.
Without that argument, the report records each package's declared source commit independently.

`artifacts/package-audit.json` contains archive hashes, sizes, identities, source commits, and assembly
paths. Pass explicit filenames if the folder contains older package versions. The manifest describes
the archive; it is not a signature or proof of a clean source tree. Native assets and transitive
imports still require the existing inspectors and fresh consumer restores.

## Before a release

Run [Verify framework](building.md#github-actions) with `All / Runtime`, then
[Verify distribution](building.md#verify-distribution-before-a-release) using that successful run ID
at the same commit. The coordinator assembles the combined feed and runs all seven framework consumers.
Keep both successful run URLs and their stage evidence. Development feeds do not establish
combined-package acceptance, and compilation does not establish physical-device execution.
See the [recorded CI evidence](verification.md#nuget-preview-2) for tested source revisions;
repeat applicable acceptance checks for the revision being released.

These workflows currently verify the MAUI integration; they do not establish acceptance for the
planned Avalonia or Uno hosts. Their [roadmap milestones](roadmap.md) require package-only consumers
and runtime CI for all six targets, including desktop macOS, Linux, and browser/WebAssembly.

The framework sample is the required consumer for the framework release. Example plugins are
optional integration coverage and have a separate manual build workflow. When releasing or changing
an example, verify its own packages and applicable consumers separately.

| Area | Required evidence |
| --- | --- |
| Portable behavior | Release tests and repeated stress runs pass; no pending requests or unclosed producer iterators |
| Native lifecycle | Updated fresh-package Android and iOS Release consumers execute reconnect checks, one simulator at a time locally |
| Android devices | Combined-feed `android-arm64` Release APK compiles; run the [physical-device check](building.md#android-arm64-and-physical-device-verification) and retain the source SHA, APK hash and device result |
| Windows | Combined package consumers compile for x64/ARM64; execute on supported Windows hosts and record remaining architecture gaps |
| Apple builds | Unsigned iOS device and Mac Catalyst Release consumers compile; Catalyst framework sample executes on the Mac runner; physical-device status recorded separately |
| Packages | All framework inspectors pass, exact-commit metadata audit passes, separate consumers restore from fresh caches |
| Performance | Record native profile source, package hashes, host/runtime, duration, percentiles and memory series; investigate regressions across comparable repeated runs |
| Documentation | API, protocol, lifecycle, threading, errors, plugin authoring, troubleshooting, build and verification guides match shipped behavior |
| Release | Update framework version and package references together, review compatibility, wait for applicable CI, then tag the verified revision |

Physical-device testing, long-running native heap profiling, x64 Apple execution, signing/store
distribution, and third-party SDK integrations remain separate verification gaps. Record actual
results before widening platform support claims. Simulator success does not establish device support.
Publish packages or create a GitHub release only as a separate, explicit release step.

## NuGet publication

Follow [Publishing to NuGet](publishing.md) after the distribution gate passes. Run the publishing
workflow with `publish=false` first and review its exact package manifest. Confirm the NuGet trust
policy and GitHub environment are configured for the five framework packages. Publication requires
a separate explicit decision, a manual run with `publish=true`, and environment approval. Verify
NuGet.org indexing and fresh public-feed consumers before marking the release accepted.

## Planned host release acceptance

Avalonia 0.2 and Uno 0.3 must each meet the [host completion criteria](roadmap.md#avalonia-integration).
Before an authorized NuGet release, audit the final host/framework packages and run independent
consumers against those exact archives on all six targets. Include native dependencies, JavaScript
and web assets, registration, lifecycle, dispatch, methods, messages, events, and native/DOM views.
Reference-plugin integration and capability limitations must have their own documented evidence.

After publication is separately authorized and completed, verify fresh restores from NuGet.org and
consumer smoke tests before recording the published release as accepted. A local Android Currency
proof or successful compilation alone does not satisfy these milestone gates. These are future
acceptance requirements, not checks already implemented in the current workflows.
