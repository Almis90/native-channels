"""Single-platform framework feeds reject wrong targets and mobile dependencies."""
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "eng"))
from package_audit import audit_platform_scope


class PlatformPackageScopeTests(unittest.TestCase):
    def check_package(self, platform, tfm, package_id="NativeChannels.Maui", dependency="NativeChannels",
                      group=None, extra=None, native_header=b'\x7fELF\x02\x01' + bytes(12) + b'\x3e\x00'):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "package.nupkg"
            with ZipFile(path, "w") as archive:
                archive.writestr(f"lib/{tfm}/{package_id}.dll", b"assembly")
                archive.writestr(f"{package_id}.nuspec", f'''<package><metadata><dependencies>
<group targetFramework="{group or tfm}"><dependency id="{dependency}" /></group>
</dependencies></metadata></package>''')
                if extra:
                    for name in ([extra] if isinstance(extra, str) else extra):
                        archive.writestr(name, b"native")
                if platform == 'linux' and package_id in ('Plugin.Currency', 'Plugin.AudioStreamer') and native_header is not None:
                    library = 'currency' if package_id.endswith('Currency') else 'audio'
                    archive.writestr(f'runtimes/linux-x64/native/libnc_{library}.so', native_header)
            audit_platform_scope(path, {"id": package_id}, platform)

    def test_macos_requires_native_target_without_maui(self):
        self.check_package('macos', 'net10.0-macos26.0', package_id='NativeChannels')
        self.check_package('macos', 'net10.0-macos26.0', package_id='NativeChannels.Native.Apple')
        with self.assertRaisesRegex(ValueError, 'frameworks'):
            self.check_package('macos', 'net10.0-maccatalyst26.0', package_id='NativeChannels')
        with self.assertRaisesRegex(ValueError, 'unexpected macos framework package'):
            self.check_package('macos', 'net10.0-macos26.0')

    def test_linux_plugin_requires_actual_native_library(self):
        for name in ('Plugin.Currency', 'Plugin.AudioStreamer'):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'native runtime assets'):
                self.check_package('linux', 'net10.0', package_id=name, native_header=None)
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'ELF64'):
                self.check_package('linux', 'net10.0', package_id=name, native_header=b'not an ELF binary')

    def test_linux_rejects_foreign_runtime_library(self):
        with self.assertRaisesRegex(ValueError, 'native runtime assets'):
            self.check_package('linux', 'net10.0', package_id='Plugin.Currency',
                               extra='runtimes/win-x64/native/currency.dll')

    def test_linux_requires_portable_assets_and_rejects_other_hosts(self):
        for name in ('NativeChannels.Core', 'NativeChannels', 'Plugin.Currency', 'Plugin.AudioStreamer'):
            self.check_package('linux', 'net10.0', package_id=name)
        for name in ('NativeChannels.Maui', 'NativeChannels.Native.Apple'):
            with self.subTest(name=name), self.assertRaisesRegex(ValueError, 'unexpected linux'):
                self.check_package('linux', 'net10.0', package_id=name)
        for dependency in ('NativeChannels.Native.Android', 'NativeChannels.Maui', 'Microsoft.Maui.Core'):
            with self.subTest(dependency=dependency), self.assertRaisesRegex(ValueError, 'dependency'):
                self.check_package('linux', 'net10.0', package_id='NativeChannels', dependency=dependency)
        with self.assertRaisesRegex(ValueError, 'frameworks'):
            self.check_package('linux', 'net10.0-windows10.0.19041', package_id='NativeChannels')
        with self.assertRaisesRegex(ValueError, 'native payload'):
            self.check_package('linux', 'net10.0', package_id='Plugin.Currency', extra='native/apple/test.xcframework/library')

    def test_each_platform_accepts_its_own_framework(self):
        for platform, tfm in [("ios", "net10.0-ios26.0"), ("maccatalyst", "net10.0-maccatalyst26.0"),
                              ("windows", "net10.0-windows10.0.19041")]:
            with self.subTest(platform=platform):
                self.check_package(platform, tfm)

    def test_browser_requires_browser_framework_without_native_payloads(self):
        for name, assets in {
            'NativeChannels': ['browser/nativechannels.mjs', 'browser/json-codec.mjs'],
            'Plugin.Currency': ['browser/currency.mjs'],
        }.items():
            self.check_package('browser', 'net10.0-browser', package_id=name, extra=assets)
            with self.assertRaisesRegex(ValueError, 'missing packaged browser adapter'):
                self.check_package('browser', 'net10.0-browser', package_id=name, extra='browser/unrelated.mjs')
        self.check_package('browser', 'net10.0', package_id='NativeChannels.Core')
        for tfm in ('net10.0', 'net10.0-android36.0'):
            with self.assertRaisesRegex(ValueError, 'frameworks'):
                self.check_package('browser', tfm, package_id='NativeChannels')
        with self.assertRaisesRegex(ValueError, 'native payload'):
            self.check_package('browser', 'net10.0-browser', package_id='NativeChannels', extra='native/android/test.aar')
        for dependency in ('NativeChannels.Native.Android', 'Microsoft.Maui.Core', 'Microsoft.WindowsAppSDK'):
            with self.assertRaisesRegex(ValueError, 'dependency'):
                self.check_package('browser', 'net10.0-browser', package_id='NativeChannels', dependency=dependency,
                                   extra=['browser/nativechannels.mjs', 'browser/json-codec.mjs'])

    def test_portable_core_is_accepted_on_each_platform(self):
        for platform in ("ios", "maccatalyst", "windows"):
            with self.subTest(platform=platform):
                self.check_package(platform, "net10.0", package_id="NativeChannels.Core")

    def test_portable_avalonia_host_is_accepted_on_each_platform(self):
        for platform in ('ios', 'maccatalyst', 'macos', 'windows', 'linux', 'browser'):
            with self.subTest(platform=platform):
                self.check_package(platform, 'net10.0', package_id='NativeChannels.Avalonia', dependency='NativeChannels.Core')
                with self.assertRaisesRegex(ValueError, 'frameworks'):
                    self.check_package(platform, 'net10.0-android36.0', package_id='NativeChannels.Avalonia')

    def test_apple_binding_is_accepted_on_apple(self):
        self.check_package("ios", "net10.0-ios26.0", package_id="NativeChannels.Native.Apple")

    def test_ios_feed_rejects_catalyst_assemblies(self):
        with self.assertRaisesRegex(ValueError, "frameworks"):
            self.check_package("ios", "net10.0-maccatalyst26.0")

    def test_apple_feed_rejects_android_dependency(self):
        with self.assertRaisesRegex(ValueError, "binding dependency"):
            self.check_package("ios", "net10.0-ios26.0", dependency="NativeChannels.Native.Android")

    def test_windows_feed_rejects_apple_binding(self):
        with self.assertRaisesRegex(ValueError, "unexpected windows framework package"):
            self.check_package("windows", "net10.0-windows10.0.19041", package_id="NativeChannels.Native.Apple")

    def test_windows_feed_rejects_mobile_dependency(self):
        with self.assertRaisesRegex(ValueError, "binding dependency"):
            self.check_package("windows", "net10.0-windows10.0.19041", dependency="NativeChannels.Native.Apple")

    def test_apple_feed_rejects_android_payload(self):
        with self.assertRaisesRegex(ValueError, "native payload"):
            self.check_package("ios", "net10.0-ios26.0", extra="native/android/plugin.aar")

    def test_windows_feed_rejects_apple_resources(self):
        with self.assertRaisesRegex(ValueError, "native payload"):
            self.check_package("windows", "net10.0-windows10.0.19041", extra="lib/example.resources.zip")

    def test_mismatched_dependency_group_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "dependency groups"):
            self.check_package("ios", "net10.0-ios26.0", group="net10.0-maccatalyst26.0")

    def test_platform_feed_accepts_reference_plugins_but_rejects_unknown_packages(self):
        self.check_package("ios", "net10.0-ios26.0", package_id="Plugin.Currency")
        with self.assertRaisesRegex(ValueError, "unexpected ios framework package"):
            self.check_package("ios", "net10.0-ios26.0", package_id="Plugin.Unknown")
