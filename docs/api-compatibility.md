# API compatibility checks

For the unreleased 0.2 preview, the combined check also applies to the expanded
platform packages. New target frameworks are additive. `NativeChannels.Avalonia`
is a new package without a published API baseline; its behavior and dependency
closure are covered by host tests and package-only consumers. See
[local preview preparation](preview-02.md).

The `0.1.0-preview.2` release checks managed public APIs against the five packages published
as `0.1.0-preview.1`. New APIs are allowed. Removing or changing existing signatures, including adding
an optional parameter in place of an existing overload, fails validation.

## Where checks run

- **Shared Core:** `dotnet pack` uses the SDK's `PackageValidationBaselineVersion`. The portable
  workflow also compares the archive with the hash-pinned public baseline and runs a negative fixture
  that must reject a removed signature with `CP0002`.
- **Release / Package gate:** after combining mobile and Windows assets, Microsoft's pinned ApiCompat
  tool compares all five final packages. Missing target frameworks are rejected. Failure prevents the
  distribution feed from being uploaded and blocks downstream distribution verification.

The baseline versions and SHA-256 hashes live in [eng/api-baseline.json](../eng/api-baseline.json).
Baseline archives come directly from NuGet.org and are checked before every comparison, including
cache hits. Candidate hashes and results are saved in `artifacts/api-compatibility/result.json`.
These checks reuse built packages; they do not rebuild native prerequisites.

## Run locally

After building the corresponding package feed:

```sh
bash eng/dotnet.sh tool restore
python3 eng/verify-api-compatibility.py --scope core
python3 eng/verify-api-compatibility.py --scope mobile
python3 eng/verify-api-compatibility.py --scope combined --packages artifacts/combined-packages
bash eng/test-api-compatibility.sh
```

Run the scope appropriate to your feed. `mobile` explicitly excludes Windows and is useful on a Mac;
it cannot satisfy the combined release gate. Only `combined` checks every published target.

## Respond to a failure

Preserve the original API and add an overload when possible. Treat changes to parameter names as
source compatibility changes too: named-argument callers depend on them. Do not regenerate baseline
hashes or suppress errors merely to make CI pass. Intentional compatibility changes need a reviewed
versioning decision, release notes, and a corresponding update to this policy.

ApiCompat checks managed API shape and target-framework availability. It does not establish protocol,
behavioral, native ABI, trimming, or runtime compatibility. The package consumers, protocol tests, and
runtime verification remain required. A preview label permits API evolution but does not make an
accidental break useful.

See Microsoft's [baseline package validation guide](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/package-validation/baseline-version-validator)
and [ApiCompat tool documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/apicompat/global-tool).
