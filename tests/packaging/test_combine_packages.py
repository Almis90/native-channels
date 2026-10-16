import importlib.util
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

ENG = Path(__file__).resolve().parents[2] / "eng"
sys.path.insert(0, str(ENG))
spec = importlib.util.spec_from_file_location("combine_packages", ENG / "combine-packages.py")
combine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(combine)
COMMIT = "a" * 40


class CombinePackagesTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.mobile, self.windows, self.output = (self.root / p for p in ("mobile", "windows", "output"))
        for directory in (self.mobile, self.windows):
            directory.mkdir()
        for name in combine.COMBINED | {"NativeChannels.Core"}:
            for directory, platforms in ((self.mobile, combine.MOBILE), (self.windows, {"windows"})):
                self.fixture(directory, name, platforms if name != "NativeChannels.Core" else {None})

    def fixture(self, directory, name, platforms, commit=COMMIT, version="1.0.0", dependency="NativeChannels.Core"):
        groups = "".join(f'<group targetFramework="net10.0-{p}1.0"><dependency id="{dependency}" version="1.0.0" /></group>' for p in sorted(platforms) if p)
        manifest = f'''<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>
<id>{name}</id><version>{version}</version><license type="expression">MIT</license><readme>README.md</readme>
<repository type="git" url="https://github.com/example/example" commit="{commit}" /><dependencies>{groups}</dependencies>
</metadata></package>'''
        for suffix, extension in (("nupkg", "dll"), ("snupkg", "pdb")):
            with zipfile.ZipFile(directory / f"{name}.{version}.{suffix}", "w") as archive:
                archive.writestr(f"{name}.nuspec", manifest)
                archive.writestr("README.md", "same readme\n")
                archive.writestr("[Content_Types].xml", f'<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="{extension}" ContentType="application/octet" /></Types>')
                for p in platforms:
                    tfm = f"net10.0-{p}1.0" if p else "net10.0"
                    archive.writestr(f"lib/{tfm}/{name}.{extension}", f"{p} {extension} payload")
                if directory == self.mobile and name.startswith("Plugin.Maui.") and suffix == "nupkg":
                    archive.writestr(f"native/android/{name}.aar", b"native Android bytes")
                    archive.writestr(f"native/apple/{name}.xcframework/Info.plist", b"native Apple bytes")

    def rewrite(self, path, entry, data):
        with zipfile.ZipFile(path) as z:
            contents = {n: z.read(n) for n in z.namelist()}
        contents[entry] = data
        with zipfile.ZipFile(path, "w") as z:
            for name, content in contents.items():
                z.writestr(name, content)

    def assemble(self):
        return combine.assemble(self.mobile, self.windows, self.output, COMMIT)

    def test_preserves_all_platform_payloads_and_symbols(self):
        results = self.assemble()
        self.assertEqual(len(results), len(combine.COMBINED) + 1)
        for name in combine.COMBINED:
            for suffix, extension in (("nupkg", "dll"), ("snupkg", "pdb")):
                with zipfile.ZipFile(self.output / f"{name}.1.0.0.{suffix}") as z:
                    for p in combine.MOBILE | {"windows"}:
                        self.assertEqual(z.read(f"lib/net10.0-{p}1.0/{name}.{extension}"), f"{p} {extension} payload".encode())
                    if name.startswith("Plugin.Maui.") and suffix == "nupkg":
                        self.assertEqual(z.read(f"native/android/{name}.aar"), b"native Android bytes")
                        self.assertEqual(z.read(f"native/apple/{name}.xcframework/Info.plist"), b"native Apple bytes")

    def keep_plugins(self, names):
        for directory in (self.mobile, self.windows):
            for path in directory.glob("Plugin.Maui.*"):
                if not any(path.name.startswith(name + ".") for name in names):
                    path.unlink()

    def test_framework_feed_needs_no_example_packages(self):
        self.keep_plugins([])
        results = combine.assemble(self.mobile, self.windows, self.output, COMMIT, plugins=[])
        self.assertEqual({record["id"] for record in results},
                         {"NativeChannels.Core", "NativeChannels", "NativeChannels.Maui"})

    def test_selected_example_does_not_require_other_examples(self):
        self.keep_plugins(["Plugin.Maui.Currency"])
        results = combine.assemble(self.mobile, self.windows, self.output, COMMIT,
                                   plugins=["Plugin.Maui.Currency"])
        self.assertEqual(len(results), 4)
        self.assertIn("Plugin.Maui.Currency", {record["id"] for record in results})

    def test_framework_scope_rejects_unrequested_examples(self):
        with self.assertRaisesRegex(ValueError, "Missing or unexpected"):
            combine.assemble(self.mobile, self.windows, self.output, COMMIT, plugins=[])

    def test_selected_example_must_exist_on_both_hosts(self):
        self.keep_plugins(["Plugin.Maui.Currency"])
        (self.windows / "Plugin.Maui.Currency.1.0.0.nupkg").unlink()
        with self.assertRaisesRegex(ValueError, "Missing or unexpected"):
            combine.assemble(self.mobile, self.windows, self.output, COMMIT,
                             plugins=["Plugin.Maui.Currency"])

    def test_deterministic_output(self):
        self.assemble()
        second = self.root / "second"
        combine.assemble(self.mobile, self.windows, second, COMMIT)
        for file in self.output.iterdir():
            self.assertEqual(file.read_bytes(), (second / file.name).read_bytes())

    def test_rejects_different_commit(self):
        self.fixture(self.windows, "NativeChannels", {"windows"}, commit="b" * 40)
        with self.assertRaisesRegex(ValueError, "source commit"):
            self.assemble()
        self.assertFalse(self.output.exists())

    def test_rejects_missing_platform(self):
        self.fixture(self.mobile, "NativeChannels", {"android", "ios"})
        with self.assertRaisesRegex(ValueError, "expected.*targets"):
            self.assemble()

    def test_rejects_multiple_package_versions(self):
        self.fixture(self.windows, "NativeChannels", {"windows"}, version="2.0.0")
        with self.assertRaisesRegex(ValueError, "Multiple versions"):
            self.assemble()

    def test_rejects_conflicting_shared_payload(self):
        self.rewrite(self.windows / "NativeChannels.1.0.0.nupkg", "README.md", b"different documentation")
        with self.assertRaisesRegex(ValueError, "conflicting shared payload"):
            self.assemble()

    def test_allows_checkout_line_endings(self):
        self.rewrite(self.windows / "NativeChannels.1.0.0.nupkg", "README.md", b"same readme\r\n")
        self.assemble()

    def test_rejects_windows_native_binding_dependency(self):
        self.fixture(self.windows, "NativeChannels", {"windows"}, dependency="NativeChannels.Native.Android")
        with self.assertRaisesRegex(ValueError, "Mobile binding"):
            self.assemble()

    def test_rejects_missing_symbols(self):
        (self.windows / "NativeChannels.1.0.0.snupkg").unlink()
        with self.assertRaisesRegex(ValueError, "Missing Windows symbols"):
            self.assemble()
        self.assertFalse(self.output.exists())

    def test_rejects_signed_inputs(self):
        self.rewrite(self.windows / "NativeChannels.1.0.0.nupkg", ".signature.p7s", b"signature")
        with self.assertRaisesRegex(ValueError, "unsigned"):
            self.assemble()

    def test_rejects_stale_symbols(self):
        for directory in (self.mobile, self.windows):
            path = directory / 'NativeChannels.1.0.0.snupkg'
            with zipfile.ZipFile(path) as z:
                data = z.read('NativeChannels.nuspec').replace(COMMIT.encode(), b'b' * 40)
            self.rewrite(path, 'NativeChannels.nuspec', data)
        with self.assertRaisesRegex(ValueError, 'symbol identity or source commit'):
            self.assemble()

    def test_rejects_mismatched_dependency_groups(self):
        path = self.windows / "NativeChannels.1.0.0.nupkg"
        with zipfile.ZipFile(path) as z:
            data = z.read("NativeChannels.nuspec").replace(b'net10.0-windows1.0', b'net10.0-windows2.0')
        self.rewrite(path, "NativeChannels.nuspec", data)
        with self.assertRaisesRegex(ValueError, "dependency groups"):
            self.assemble()

    def test_rejects_nonempty_output(self):
        self.output.mkdir()
        (self.output / "existing.nupkg").write_bytes(b"existing")
        with self.assertRaisesRegex(ValueError, "Output directory must be empty"):
            self.assemble()


if __name__ == "__main__":
    unittest.main()
