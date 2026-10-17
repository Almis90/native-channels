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
                      group=None, extra=None):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "package.nupkg"
            with ZipFile(path, "w") as archive:
                archive.writestr(f"lib/{tfm}/{package_id}.dll", b"assembly")
                archive.writestr(f"{package_id}.nuspec", f'''<package><metadata><dependencies>
<group targetFramework="{group or tfm}"><dependency id="{dependency}" /></group>
</dependencies></metadata></package>''')
                if extra:
                    archive.writestr(extra, b"native")
            audit_platform_scope(path, {"id": package_id}, platform)

    def test_macos_requires_native_target_without_maui(self):
        self.check_package('macos', 'net10.0-macos26.0', package_id='NativeChannels')
        self.check_package('macos', 'net10.0-macos26.0', package_id='NativeChannels.Native.Apple')
        with self.assertRaisesRegex(ValueError, 'frameworks'):
            self.check_package('macos', 'net10.0-maccatalyst26.0', package_id='NativeChannels')
        with self.assertRaisesRegex(ValueError, 'unexpected macos framework package'):
            self.check_package('macos', 'net10.0-macos26.0')

    def test_each_platform_accepts_its_own_framework(self):
        for platform, tfm in [("ios", "net10.0-ios26.0"), ("maccatalyst", "net10.0-maccatalyst26.0"),
                              ("windows", "net10.0-windows10.0.19041")]:
            with self.subTest(platform=platform):
                self.check_package(platform, tfm)

    def test_portable_core_is_accepted_on_each_platform(self):
        for platform in ("ios", "maccatalyst", "windows"):
            with self.subTest(platform=platform):
                self.check_package(platform, "net10.0", package_id="NativeChannels.Core")

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

    def test_framework_feed_rejects_example_plugin(self):
        with self.assertRaisesRegex(ValueError, "unexpected ios framework package"):
            self.check_package("ios", "net10.0-ios26.0", package_id="Plugin.Maui.Currency")
