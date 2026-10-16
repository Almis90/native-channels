"""Android iteration must not quietly consume a combined or Apple-dependent archive."""
import sys
import tempfile
import unittest
from pathlib import Path
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "eng"))
from package_audit import audit_android_scope


class AndroidPackageScopeTests(unittest.TestCase):
    def check_package(self, package_id="NativeChannels.Maui", frameworks=("net10.0-android36.0",),
                      groups=None, dependency="NativeChannels", extra=None):
        if groups is None:
            groups = frameworks
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "package.nupkg"
            with ZipFile(path, "w") as archive:
                for tfm in frameworks:
                    archive.writestr(f"lib/{tfm}/{package_id}.dll", b"assembly")
                dependencies = "".join(f'<group targetFramework="{tfm}"><dependency id="{dependency}" /></group>' for tfm in groups)
                archive.writestr(f"{package_id}.nuspec", f'<package><metadata><dependencies>{dependencies}</dependencies></metadata></package>')
                if extra:
                    archive.writestr(extra, b"native")
            audit_android_scope(path, {"id": package_id})

    def test_android_package_is_accepted(self):
        self.check_package()

    def test_portable_core_is_accepted(self):
        self.check_package(package_id="NativeChannels.Core", frameworks=("net10.0",))

    def test_combined_assemblies_are_rejected(self):
        with self.assertRaisesRegex(ValueError, "frameworks"):
            self.check_package(frameworks=("net10.0-android36.0", "net10.0-ios26.0"))

    def test_other_dependency_group_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "dependency groups"):
            self.check_package(groups=("net10.0-android36.0", "net10.0-ios26.0"))

    def test_apple_binding_dependency_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "Apple binding dependency"):
            self.check_package(dependency="NativeChannels.Native.Apple")

    def test_apple_native_payload_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "native payload"):
            self.check_package(extra="native/apple/Example.xcframework/Info.plist")

    def test_windows_payload_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "native payload"):
            self.check_package(extra="native/windows/example.dll")

    def test_apple_package_identity_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "unexpected Android feed package"):
            self.check_package(package_id="NativeChannels.Native.Apple")
