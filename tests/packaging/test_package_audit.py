import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location("package_audit", Path(__file__).resolve().parents[2] / "eng/package_audit.py")
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)

COMMIT = "a" * 40
NUSPEC = f'''<package xmlns="http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"><metadata>
<id>Example</id><version>1.0.0</version><license type="expression">MIT</license><readme>README.md</readme>
<repository type="git" url="https://github.com/example/example" commit="{COMMIT}" />
</metadata></package>'''


class PackageAuditTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = Path(self.directory.name) / "Example.1.0.0.nupkg"

    def write_package(self, nuspec=NUSPEC, readme=True, extra=None):
        with zipfile.ZipFile(self.path, "w") as archive:
            archive.writestr("Example.nuspec", nuspec)
            archive.writestr("lib/net10.0/Example.dll", b"test fixture")
            if readme:
                archive.writestr("README.md", "Example package")
            if extra:
                entry = zipfile.ZipInfo()
                # Preserve deliberately unsafe names even on Windows, where ZipInfo normalizes paths.
                entry.filename = extra
                archive.writestr(entry, "unexpected")

    def test_valid_manifest_and_exact_commit(self):
        self.write_package()
        result = audit.audit_package(self.path, COMMIT)
        self.assertEqual(result["sourceCommit"], COMMIT)
        self.assertEqual(len(result["sha256"]), 64)
        self.assertEqual(result["assemblies"], ["lib/net10.0/Example.dll"])

    def test_rejects_wrong_source_commit(self):
        self.write_package()
        with self.assertRaisesRegex(ValueError, "differs from expected"):
            audit.audit_package(self.path, "b" * 40)

    def test_rejects_missing_readme(self):
        self.write_package(readme=False)
        with self.assertRaisesRegex(ValueError, "README"):
            audit.audit_package(self.path)

    def test_rejects_invalid_metadata(self):
        for original, replacement in [("MIT", "unknown"), (COMMIT, ""), ("<version>1.0.0", "<version>2.0.0")]:
            with self.subTest(original=original):
                self.write_package(NUSPEC.replace(original, replacement))
                with self.assertRaises(ValueError):
                    audit.audit_package(self.path)

    def test_rejects_unsafe_entries(self):
        for name in ["../secret", "/absolute", "folder/../../secret", "folder\\secret"]:
            with self.subTest(name=name):
                self.write_package(extra=name)
                with self.assertRaisesRegex(ValueError, "unsafe"):
                    audit.audit_package(self.path)

    def test_rejects_duplicate_entries(self):
        with self.assertWarns(UserWarning):
            self.write_package(extra="README.md")
        with self.assertRaisesRegex(ValueError, "duplicate"):
            audit.audit_package(self.path)

    def test_browser_scope_accepts_dual_targets_but_rejects_unrelated_targets(self):
        for targets, valid in [
            (['net10.0-browser1.0'], True),
            (['net10.0-browser1.0', 'net10.0-browserwasm1.0'], True),
            (['net10.0-browserwasm1.0'], False),
            (['net10.0-browser1.0', 'net10.0-android36.0'], False),
        ]:
            with self.subTest(targets=targets):
                groups = ''.join(f'<group targetFramework="{tfm}"/>' for tfm in targets)
                with zipfile.ZipFile(self.path, 'w') as archive:
                    archive.writestr('Plugin.Currency.nuspec',
                        f'<package><metadata><dependencies>{groups}</dependencies></metadata></package>')
                    archive.writestr('browser/currency.mjs', 'export {}')
                    for tfm in targets:
                        archive.writestr(f'lib/{tfm}/Plugin.Currency.dll', b'fixture')
                if valid:
                    audit.audit_platform_scope(self.path, {'id': 'Plugin.Currency'}, 'browser')
                else:
                    with self.assertRaisesRegex(ValueError, 'unexpected browser frameworks'):
                        audit.audit_platform_scope(self.path, {'id': 'Plugin.Currency'}, 'browser')
