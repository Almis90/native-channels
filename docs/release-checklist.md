# Release checklist

Use these checks before tagging or distributing NativeChannels. Record the exact source revision,
package hashes, and platform results in [verification](verification.md). Compilation and runtime
execution are separate evidence. No workflow publishes packages automatically.

## Implemented checks

- Repeatable portable stress tests for reentrant requests, handler ownership, concurrent disposal,
  independent subscriptions, and view connection lifetimes.
- A regression test for throwing cancellation callbacks during in-memory transport disposal.
- A separate benchmark project for codec throughput/allocations and managed channel round trips.
- Repeated native view reconnect verification in the real package-only MAUI sample.
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

Run [Verify framework](building.md#github-actions) with `All / Runtime`, then the separate release package gate at the same source commit. Keep the successful run URLs and stage evidence. Use the combined feed for release consumers (`package_source=Release` on every platform). Single-platform
development feeds do not establish combined-package acceptance. One green sample run is not a full-platform release pass. See the [recorded CI evidence](verification.md#framework-ci-verification) for its tested source revision;
repeat applicable acceptance checks for the revision being released.

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
| Performance | Record benchmark host, runtime, source revision, allocations, uncertainty, and workload; investigate measured regressions |
| Documentation | API, protocol, lifecycle, threading, errors, plugin authoring, troubleshooting, build and verification guides match shipped behavior |
| Release | Update framework version and package references together, review compatibility, wait for applicable CI, then tag the verified revision |

Physical-device testing, long-running native heap profiling, x64 Apple execution, signing/store
distribution, and third-party SDK integrations remain separate verification gaps. Record actual
results before widening platform support claims. Simulator success does not establish device support.
Publish packages or create a GitHub release only as a separate, explicit release step.
