# Currency in Avalonia Linux

The shared Desktop head selects `net10.0` on Linux, with Avalonia 12.1.4's X11 backend.
It consumes Core, NativeChannels and Currency from local NuGet packages. Only the shared
application UI is a project reference; no MAUI, mobile binding or WinUI dependency is allowed.

The Linux transport runs serialized channels between two owned .NET endpoints, as the Windows
transport does. A module supplies `LinuxFactory` / `ILinuxChannelModule` and can call Linux system
APIs through .NET interop. Currency calls the system ICU C API directly for currency validation
and localized symbols. It does not bundle a currency table or use a managed lookup substitute.
`AddNativeChannels(options => options.AddCurrency())` and `AddCurrency()` retain the same API.

## Build and execute

On Ubuntu 24.04 x64, install the desktop runtime and test-display dependencies:

```bash
sudo apt-get update
sudo apt-get install -y libicu74 libx11-6 libice6 libsm6 libfontconfig1 libxrandr2 libxi6 libxcursor1 xvfb openbox xdotool wmctrl scrot fonts-dejavu-core
bash eng/bootstrap.sh
bash eng/dotnet.sh test -c Release
bash eng/dotnet.sh test tests/NativeChannels.Linux.Tests -c Release
python3 -m unittest discover -s tests/packaging
```

Start with an empty `artifacts/packages` feed; preserve packages from earlier platform builds.
The following creates Linux development packages with `NativeChannelsLinuxOnly=true`, restores
the consumer into a fresh cache, checks archive hashes, and builds Release:

```bash
bash eng/pack-linux.sh
bash eng/restore-currency-avalonia.sh linux -r linux-x64
bash eng/dotnet.sh build samples/Avalonia/Currency/Currency.Desktop -c Release -r linux-x64 --no-restore
xvfb-run -a python3 eng/verify-currency-linux.py --display-kind xvfb
xvfb-run -a -s '-screen 0 1280x900x24' bash -c 'openbox > /tmp/nativechannels-openbox.log 2>&1 & python3 eng/verify-currency-linux.py --display-kind xvfb --interactive-close'
```

In an existing X11 desktop session, omit `xvfb-run` and `--display-kind xvfb`. Launch interactively with:

```bash
bash eng/dotnet.sh run --project samples/Avalonia/Currency/Currency.Desktop -c Release -r linux-x64 --no-build
```

`CurrencyDesktopTarget=linux` can also select the head explicitly. The Linux development archives
contain `net10.0` assemblies with Linux adapters and runtime OS checks. They are scoped test feeds,
not combined multi-platform release packages; do not use them as portable implementations on another OS.
The ICU loader supports versioned or unversioned system libraries with ICU major versions 60–99;
Ubuntu 24.04's ICU 74 is the tested version. Missing ICU produces an actionable startup/lookup error.
The current .NET culture selects the symbol locale. Results and accepted currency codes follow the
installed ICU data; no guarantee is made that all operating systems return identical symbols.

## Verification and scope

The [Linux workflow](../../../../.github/workflows/shared-hosting-linux.yml) runs independently of
the MAUI/mobile verification jobs. The native tests cover method reentrancy, errors, binary messages,
independent streams, cancellation, concurrent replies, module ownership and rollback, plus actual ICU
lookups. The packaged desktop proof checks USD/EUR/GBP/JPY, normalization, errors, concurrency,
cancellation/recovery, and asynchronous disposal. It runs both `--verify` automatic shutdown and a
normal window-manager close through EWMH (`wmctrl`), after waiting for the manager to start.
A PASS line cannot hide a nonzero exit, duplicate result, or missing disposal.

Evidence is saved under `artifacts/shared-hosting-linux/linux-x64/{verification,window-close}`.
It records OS/kernel/SDK, source commit and dirty state, hashes of the source files, restored package
hashes, application hash, result logs, and (for normal window closure) the loaded ICU paths. If `scrot`
is installed, the window-close run also saves a screenshot. The display can be a real X11 desktop
or Xvfb; these environments must be identified separately in the verification record.

This is the Linux **Currency method-call proof** for milestone 0.2. The transport's native unit tests
do not establish Avalonia messages/events UI integration. A reusable Avalonia host package, native
view embedding, the other reference plugins, Wayland, ARM64, trimming/AOT and combined NuGet release
acceptance remain separate work. See [executed evidence](../../../../docs/verification.md#avalonia-on-linux).

API references: [ICU currency API](https://unicode-org.github.io/icu-docs/apidoc/released/icu4c/ucurr_8h.html)
and [Avalonia Linux deployment](https://docs.avaloniaui.net/docs/platform-specific-guides/linux).
