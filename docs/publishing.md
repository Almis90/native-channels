# Publishing to NuGet

The first framework preview, `0.1.0-preview.1`, is
[available on NuGet.org](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.1).
Applications install `NativeChannels.Maui`, with framework dependencies resolved automatically;
see [getting started](getting-started.md).

This guide describes the maintainer release process. The [verification record](verification.md#nuget-publication-2026-10-17)
identifies the published source, accepted archives, and public-feed checks.

## Release contents

Only these five framework packages belong to this release:

| Package | Purpose |
| --- | --- |
| `NativeChannels.Core` | Portable messaging, codecs, serialization, and channel contracts |
| `NativeChannels` | Platform transports and explicit module registration |
| `NativeChannels.Maui` | MAUI startup, dispatch, and native view integration |
| `NativeChannels.Native.Android` | Generic Android binding and bridge |
| `NativeChannels.Native.Apple` | Generic iOS and Mac Catalyst binding and bridge |

Each package includes a NuGet-specific README; a corresponding `.snupkg` contains managed symbols.
Currency, AudioStreamer, VolumeMonitor, and PdfViewer remain reference plugins and are not included.
Avalonia and Uno packages are [planned](roadmap.md), not part of the current release.

## One-time account setup

Use [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing)
with GitHub Actions. The publishing job exchanges its workflow identity for a short-lived key;
there is no long-lived NuGet API key stored in this repository.

Configure the GitHub environment `nuget` with:

- A deployment branch rule allowing only the branch `main`.
- Required reviewer `Almis90`. Self-review is allowed so the repository owner can approve a manually started release.
- Environment variable `NUGET_USER=Almis`, the NuGet.org username rather than the GitHub username.

Create and approve this policy in the NuGet account's **Trusted Publishing** settings:

| Field | Value |
| --- | --- |
| Package owner | `Almis` |
| Policy name | `NativeChannels GitHub releases` |
| Provider | GitHub Actions |
| Repository owner | `Almis90` |
| Repository | `native-channels` |
| Workflow file | `publish-nuget.yml` |
| Environment | `nuget` |
| Permissions | Push new packages and versions; no unlist/relist permission |

Limit the policy to the five exact package IDs above, one per line. Do not use a wildcard that also
grants access to example or future host packages. The policy must be saved before publishing;
checking in the workflow does not create the NuGet account policy.

## Validate the candidate

1. Commit the intended version, package inputs, and documentation to `main`. Update version pins and consumer references together.
2. Run **Verify framework** with `All / Runtime`.
3. Run **Verify distribution** with that successful framework run ID at the **same commit**.
4. Run **Release / Publish NuGet** from `main`, supplying the successful distribution run ID and exact version. Leave **publish** disabled.

For example, replace `DISTRIBUTION_RUN_ID` with the actual numeric run ID:

```sh
gh workflow run publish-nuget.yml --ref main \
  -f distribution_run=DISTRIBUTION_RUN_ID \
  -f version=0.1.0-preview.2 -F publish=false
```

The dry run checks the successful workflow's repository, event, source, and artifacts. It requires
all seven combined-feed consumer results, their expected compile/execution flags, and identical feed
hashes. It then audits all five package identities, versions, source commits, README/license metadata,
archive hashes, and Portable PDB symbol archives. An extra plugin package or missing archive fails validation.

It uploads `nuget-candidate` with the exact ten archives and `release-manifest.json`. It does not
rebuild packages, request a NuGet credential, or upload anything to NuGet.org. Review the manifest
and workflow summary alongside the [release checklist](release-checklist.md).

Artifacts expire after seven days. Expired inputs require fresh verification. Any change to the
release source, including packaged documentation, requires same-commit checks again. Do not use
older results as evidence for a newer revision. The final distribution gate executes Android x64
and the iOS simulator; its other configurations compile only. Physical-device and desktop results
remain separately recorded in [verification](verification.md).

## Publish after approval

Publication is a separate, explicit release decision. Start the same workflow from `main` with
**publish** enabled, the same accepted version, and its successful same-commit distribution run.
Review and approve the `nuget` environment deployment in GitHub Actions.

The job rechecks candidate hashes before authenticating with the pinned
[NuGet login action](https://github.com/NuGet/login). It checks existing package versions before the
first upload, then submits packages in dependency order followed by their symbol archives.
No GitHub release or tag is created. Pushes, pull requests, and tags never trigger publication.

A successful upload command is not confirmation that NuGet validation and indexing have finished.
Afterward:

- Confirm all five package versions are listed and their metadata, dependencies, README, and symbols are accepted.
- Restore a separate MAUI application from NuGet.org with a clean cache, and repeat the applicable package-only consumer checks.
- Record public-feed results and update the installation/status documentation only after those checks pass.

## Partial uploads and retries

NuGet versions cannot be overwritten. If some uploads succeed and a later one fails, retain the
candidate and inspect the job output and NuGet package status before retrying.

The workflow skips an already indexed package only when all archived file contents match the
candidate, ignoring NuGet's added repository signature. A different package with the same ID/version
stops the entire upload before any new package is sent. Symbol retries use NuGet's duplicate handling.
If an earlier upload is still indexing, wait for its status to settle before retrying the unchanged
candidate. Never substitute rebuilt archives into an approved release.

See Microsoft's [symbol package guidance](https://learn.microsoft.com/en-us/nuget/create-packages/symbol-packages-snupkg)
for symbol validation and indexing failures. A failed or incomplete publication is not an accepted release.
