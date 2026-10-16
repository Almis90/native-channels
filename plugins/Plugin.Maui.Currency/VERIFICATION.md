# Currency verification

Fourteen portable facade tests, two Kotlin currency tests, and the Swift currency checks passed on
2026-10-15. They cover supported native symbols, normalization, invalid codes, and managed error/cancellation behavior.

## Build verification

On 2026-10-15 the Android adapter and Apple XCFramework compiled, including iOS device, simulator,
and Mac Catalyst slices. This is native compilation, not application runtime verification. The
current preview still requires fresh package-only consumer acceptance on each target. See the
[platform verification record](../../docs/verification.md).

## Shared package in MAUI and Avalonia Android

On 2026-10-17, both Release apps passed on an Android API 35 x64 emulator using identical Currency
package bytes restored into separate caches. Avalonia resolved no MAUI dependencies. See the
[shared-hosting evidence](../../docs/verification.md#shared-hosting-on-android) for the successful CI
job, source revision, package hash, tested scenarios and scope.

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

Verify current-preview packages on Android, iOS, Catalyst, and Windows; exercise supported physical
devices and Windows architectures independently. Record runtime, package hashes, source SHA, and
locale when adding results. See the [Windows acceptance matrix](../../docs/windows-integration.md#combined-platform-packages).
