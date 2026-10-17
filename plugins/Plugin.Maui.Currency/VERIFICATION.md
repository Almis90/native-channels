# Currency verification

Fourteen portable facade tests, two Kotlin currency tests, and the Swift currency checks passed on
2026-10-15. They cover supported native symbols, normalization, invalid codes, and managed error/cancellation behavior.

## Build verification

On 2026-10-15 the Android adapter and Apple XCFramework compiled, including iOS device, simulator,
and Mac Catalyst slices. This is native compilation, not application runtime verification. The
current preview still requires fresh package-only consumer acceptance on each target. See the
[platform verification record](../../docs/verification.md).

## Shared package in MAUI and Avalonia

On 2026-10-17, both Release apps passed on an Android API 35 x64 emulator using identical Currency
package bytes restored into separate caches. Avalonia resolved no MAUI dependencies. See the
[shared-hosting evidence](../../docs/verification.md#shared-hosting-on-android) for the successful CI
job, source revision, package hash, tested scenarios and scope.

At PR head `4f648cb` on the same date, both iOS Release apps passed on an iPhone 17 Pro ARM64
simulator, and the Android host pair passed again. Each platform's hosts restored identical shared
archives into separate fresh caches; neither Avalonia graph included MAUI. The iOS run covered the
same native symbols, normalization, errors, invalid input, concurrency, cancellation and recovery.
MAUI built with zero warnings/errors; Avalonia iOS had one `IL2104` trimming warning from
`Avalonia.DesignerSupport` 12.1.4 and zero errors. See the
[iOS evidence](../../docs/verification.md#shared-hosting-on-ios) for exact revisions, package hash,
CI jobs and limits. This is simulator method-call evidence, not full Avalonia lifecycle acceptance.

At clean source `3118288` on 2026-10-17, both Windows Release applications passed on a physical
Windows 11 x64 PC with matching Currency archives and results. Avalonia used Win32 without MAUI
or Windows App SDK dependencies and exited after normal window/session cleanup. All 17 Windows
Currency native API tests passed. See the [Windows shared-hosting evidence](../../docs/verification.md#shared-hosting-on-windows)
for exact package hashes and limits; ARM64 compilation is separate from physical ARM64 execution.

## Windows development preview

The Windows adapter uses WinRT `CurrencyIdentifiers` and `CurrencyFormatter` and exposes the same currency-code lookup contract. Native
API tests and a separate package-only MAUI consumer are available through:

```powershell
./eng/verify-windows.ps1 -Currency
```

The current preview needs fresh combined-package Windows consumer acceptance. Native locale data
can return different valid symbol widths, such as the two yen forms; tests should check the contract
without assuming one host's locale database.

## Remaining checks

Verify final combined-feed packages on Android, iOS, Catalyst, and Windows; exercise supported physical
devices and Windows architectures independently. Record runtime, package hashes, source SHA, and
locale when adding results. See the [Windows acceptance matrix](../../docs/windows-integration.md#combined-platform-packages).
