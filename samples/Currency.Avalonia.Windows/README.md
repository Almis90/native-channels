# Currency in Avalonia Windows

This package-only .NET 10 Windows desktop application consumes the same `Plugin.Maui.Currency`
archive as the [MAUI sample](../Currency.Sample/README.md). It pins Avalonia 12.1.4, uses its Win32
backend, and has no MAUI, WinUI/Windows App SDK, or library project dependency. Currency calls the
existing WinRT adapter through the shared channel transport.

The project targets `net10.0-windows10.0.19041.0` to access the Windows Currency package. Its declared
minimum OS is Windows 10 build 17763; this is a build setting, not evidence that every Avalonia
version or OS/architecture combination was tested. See the [verification record](../../docs/verification.md)
for environments actually executed.

## Registration and lifetime

The application uses `IClassicDesktopStyleApplicationLifetime` with one window. Its `CurrencyView`
and `CurrencySession` are shared with the Android and iOS samples. The session registers
`AddNativeChannels(options => options.AddCurrency())` and `AddCurrency()` in a service provider.
Normal window closing cancels outstanding work and awaits provider/session disposal before closing.
Forced process termination and OS shutdown can prevent managed cleanup; those paths are not claimed
by this sample's close verification.

The view automatically tests USD/EUR/GBP/JPY lookups, normalization, structured native errors,
invalid input, concurrent requests, cancellation and recovery. The window remains available for
interactive lookups. Passing `--verify` closes it through the normal cleanup path after the checks.
`CURRENCY_AVALONIA_RESULT` optionally selects an atomic result file; cleanup failures replace the
earlier result and cause a nonzero exit code.

## Verify both package consumers

From PowerShell on Windows, with the [pinned SDK and Windows workload](../../docs/building.md):

```powershell
./eng/verify-shared-hosting-windows.ps1
```

This packs the Windows framework and Currency, restores the MAUI and Avalonia applications into
separate fresh caches, runs the Windows Currency API tests, and publishes both Release applications.
It verifies identical Core, transport and Currency archives, rejects project references and mobile
bindings, and rejects MAUI or Windows App SDK dependencies in Avalonia. It then executes both apps
and compares the complete native results. Avalonia must also exit successfully after disposal.
Applications are unpackaged and self-contained; no MSIX installation or certificate trust is needed.

Use a prebuilt Windows-only feed or compile for another architecture:

```powershell
./eng/verify-shared-hosting-windows.ps1 -PackageFeed artifacts/windows/packages
./eng/verify-shared-hosting-windows.ps1 -RuntimeIdentifier win-arm64 -BuildOnly
```

Runtime checks require the native OS architecture. The script retains isolated evidence under
`artifacts/shared-hosting-windows/<runtime>/<run>/`: package hashes, compilation/environment details,
native test results and, only after both applications execute successfully, `verification.json`.

**Shared hosting / Windows Currency** runs whenever **Verify framework** selects Windows. It reuses
that run's framework feed, builds both applications for x64 and ARM64, verifies shared packages and
executes native API tests on the x64 runner. Hosted CI uses `-BuildOnly`: it does not claim desktop UI
execution or ARM64 runtime acceptance. Execute the default command on the matching Windows desktop
for that evidence. Android and iOS retain their separate emulator/simulator runtime jobs.

This is the Currency method-call proof. Native WinUI view embedding, other channel contracts,
multi-window lifecycle, other reference plugins, MSIX deployment and complete Avalonia host packages
remain part of [milestone 0.2](../../docs/roadmap.md#avalonia-integration). Windows-only development
archives are separate from final combined-package release acceptance.
