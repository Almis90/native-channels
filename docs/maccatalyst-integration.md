# Mac Catalyst integration

The Apple bridge shares its Foundation-based implementation with iOS. The native build creates
arm64 and x64 Mac Catalyst slices using the `-macabi` target, and the binding multi-targets
`net10.0-ios` and `net10.0-maccatalyst`.

The package consumer includes a Mac Catalyst target. Native framework compilation, managed binding
compilation, application linking, and runtime execution are separate checks. Consult the verification
record rather than assuming that an iOS simulator result also verifies Mac Catalyst.

Third-party iOS SDKs need their own Mac Catalyst-compatible binaries. UIKit compatibility does not
make an iOS device framework usable in a Mac Catalyst process.

Native view factories use UIKit. The framework builder includes the Mac SDK's
`System/iOSSupport` framework, header, and library search paths for Catalyst. Package inspection
requires the generic view host and factory protocols in every Apple slice, so a framework compiled
without its UIKit surface cannot pass verification. See [native views](native-views.md).
