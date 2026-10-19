# Publishing to NuGet

The 0.2 preview is built and tested locally, then published through NuGet Trusted
Publishing. Only `publish-nuget.yml` runs for publication; it does not build
packages or execute tests. Direct local publication with an API key remains an
alternative. The five-package 0.1 process below is historical.

## Trusted Publishing of locally accepted packages {#trusted-preview}

Complete [local candidate acceptance](preview-02.md), then prepare the immutable
release directory using the `prepare` command in the next section. Bundle it:

```sh
python eng/trusted-preview.py bundle --directory artifacts/nuget-0.2.0-preview.1 \
  --output artifacts/NativeChannels.0.2.0-preview.1.accepted.zip
```

Keep the printed SHA-256. Upload the ZIP unchanged to a draft prerelease named
`v0.2.0-preview.1` in `Almis90/native-channels`. The draft is only a staging
location; creating it does not publish to NuGet. Keep the source revision in the
bundle and package metadata unchanged, even when release tooling is committed later.

The NuGet policy `NativeChannels GitHub releases` must allow exactly the six
package IDs listed below, with push permission, repository `Almis90/native-channels`,
workflow `publish-nuget.yml`, and environment `nuget`. The GitHub `nuget` environment
retains its main-only branch rule and required reviewer. Set its `NUGET_USER`
variable to the NuGet username `Almis`.

Manually dispatch **Release / Publish NuGet** on `main` with the numeric draft
release ID as `candidate_release`, the reviewed ZIP hash as `bundle_sha256`, exact
`version`, and `publish=true` when publication is authorized. `publish=false`
only validates. No other workflow is required or dispatched by this path.
Use `[skip ci]` on prerequisite commits when all other workflows must remain idle.

GitHub requires push access to read draft releases. The validation job therefore
has job-scoped `contents: write`, used only to download the staged bundle; checkout
does not persist its credential. The publishing job retains `contents: read`.

Before authentication, the workflow checks the asset hash, safely extracts it,
replays the local acceptance gate, matches the receipt and candidate against
the committed verification record, and confirms the tested source is merged
into `main`. After environment approval it rechecks the archives and public
versions, obtains a temporary credential from NuGet/login, and uploads the six
packages and their symbols. It retains a submission report, including partial
failures. It never rebuilds the accepted archives or claims tests ran in Actions.

After upload, verify NuGet indexing, symbols and fresh public-feed consumers as
described below. A successful publishing job alone is not public-feed acceptance.

## Publish the locally accepted 0.2 preview {#local-preview}

First complete [candidate preparation and acceptance](preview-02.md). Retain the
twelve exact archives, API report, acceptance receipt and ten consumer summaries.
Create a portable release directory with the local publisher:

```powershell
$previewArgs = @('eng/publish-preview.py', 'prepare',
  '--directory', 'artifacts/nuget-0.2.0-preview.1',
  '--candidate', 'artifacts/preview-02-r3/candidate',
  '--acceptance', 'artifacts/preview-02-r3/local-acceptance.json',
  '--api-report', 'artifacts/preview-02-r3/api-compatibility.json')
Get-ChildItem artifacts/preview-02-r3/results -Recurse -Filter summary.json |
  ForEach-Object { $previewArgs += @('--summary', $_.FullName) }
python @previewArgs
python eng/publish-preview.py preflight --directory artifacts/nuget-0.2.0-preview.1
```

Preparation replays the acceptance gate and checks NuGet archive/symbol metadata.
It copies the original archives unchanged and records hashes for the complete
release, API report, acceptance receipt and summaries. `verify` replays these
checks offline. `preflight` additionally checks all six public package versions:
an existing version with different contents stops publication before any upload.
NuGet's added repository signature is excluded from that content comparison.

Merge the tested source and release tooling into `main` before publishing. Use
`[skip ci]` in pushed and merge commit messages when workflows must stay idle;
see [GitHub's skip rules](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/skip-workflow-runs).
Keep the package source SHA unchanged: later documentation/tooling commits and
the merge commit are not the source from which these archives were built.

Local publication needs a NuGet.org API key; GitHub Trusted Publishing cannot
authenticate a local process. Create a short-lived key in the NuGet account
with push permissions for exactly these six IDs, including permission to create
the new Avalonia package:

```text
NativeChannels.Core
NativeChannels.Native.Android
NativeChannels.Native.Apple
NativeChannels
NativeChannels.Maui
NativeChannels.Avalonia
```

After the release decision, run:

```powershell
python eng/publish-preview.py publish --directory artifacts/nuget-0.2.0-preview.1 `
  --publish-version 0.2.0-preview.1 --submission-report artifacts/nuget-preview1-submission.json `
  --key-dialog
```

The masked dialog holds the key only in memory. Omit `--key-dialog` to enter it
in an interactive terminal with hidden input. Never put it in chat, shell
arguments, a checked-in file or a log. The publisher sends the key in the
authenticated HTTPS header using NuGet's [package](https://learn.microsoft.com/en-us/nuget/api/package-publish-resource)
and [symbol](https://learn.microsoft.com/en-us/nuget/api/symbol-package-publish-resource)
upload protocols; it does not pass it to a child process or follow redirects.

The command checks that the source is merged into remote `main`, rechecks
acceptance after credential entry, then uploads packages in dependency order
followed by symbols. It records each attempted submission outside the immutable
release directory. If an upload fails, preserve that report and check NuGet's
status before retrying the same archives with a new report filename. Identical
indexed packages are skipped. An HTTP 409 for symbols means indexing is pending,
not verified success. Do not rebuild or overwrite an accepted package version.

After NuGet finishes processing:

```sh
python eng/publish-preview.py verify-public --directory artifacts/nuget-0.2.0-preview.1
```

This downloads all six public packages and compares their contents with the
accepted release. Separately check symbol validation in the NuGet account and
restore fresh MAUI and Avalonia consumers from NuGet.org with isolated caches.
Record public-feed checks in [verification](verification.md) before marking the
release published and accepted. No tags or GitHub releases are created by this tool.

## Existing 0.1 workflow

The remainder records the old five-package process used for 0.1. The current
`publish-nuget.yml` uses the six-package local acceptance inputs above; the old
`distribution_run` dispatch examples are not current commands.

The earlier framework preview, `0.1.0-preview.2`, is
[available on NuGet.org](https://www.nuget.org/packages/NativeChannels.Maui/0.1.0-preview.2).
Applications install `NativeChannels.Maui`, with framework dependencies resolved automatically;
see [getting started](getting-started.md).

This guide describes the maintainer release process. The [verification record](verification.md#nuget-preview-2)
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
