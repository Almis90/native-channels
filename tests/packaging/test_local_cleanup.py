import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


def load(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).resolve().parents[2] / 'eng' / filename)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


consumer = load('clean_consumer', 'clean-consumer.py')
local = load('clean_local', 'clean-local.py')


class LocalCleanupTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name).resolve()
        self.artifacts = self.root / 'artifacts'
        self.artifacts.mkdir()
        self.project = self.root / 'samples/Maui/ChannelExplorer'
        (self.project / 'bin').mkdir(parents=True)
        (self.project / 'obj').mkdir()
        self.cache = self.artifacts / 'consumer-packages.previous'
        self.cache.mkdir()
        self.marker = self.artifacts / 'sample-cache-path.txt'
        self.marker.write_text(str(self.cache))

    def test_consumer_releases_previous_cache_and_outputs(self):
        consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
        self.assertFalse(self.cache.exists())
        self.assertFalse(self.marker.exists())
        self.assertFalse((self.project / 'obj').exists())

    def test_shared_cache_survives_consumer_cleanup(self):
        (self.artifacts / 'audio-cache-path.txt').write_text(str(self.cache))
        consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
        self.assertTrue(self.cache.exists())

    def test_external_cache_is_rejected_before_mutation(self):
        self.marker.write_text(str(self.root))
        with self.assertRaises(ValueError):
            consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
        self.assertTrue((self.project / 'obj').exists())
        self.assertTrue(self.cache.exists())

    def test_symlinked_outputs_are_rejected(self):
        (self.project / 'bin').rmdir()
        (self.project / 'bin').symlink_to(self.artifacts, target_is_directory=True)
        with self.assertRaises(ValueError):
            consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
        self.assertTrue(self.cache.exists())

    def test_pruning_preserves_marker_and_assets_references(self):
        active = self.artifacts / 'consumer-packages.active'
        active.mkdir()
        orphan = self.artifacts / 'consumer-packages.orphan'
        orphan.mkdir()
        (self.project / 'obj/project.assets.json').write_text(json.dumps({'packageFolders': {str(active): {}}}))
        (self.artifacts / 'consumer-packages.link').symlink_to(self.root, target_is_directory=True)
        (self.artifacts / 'packages').mkdir()
        planned = local.candidates(self.root, ['samples/Maui/ChannelExplorer/Sample.csproj'])
        self.assertEqual(planned, [orphan])

    def test_external_project_directory_is_rejected(self):
        consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
        self.project.rmdir()
        with tempfile.TemporaryDirectory() as external:
            outside = Path(external)
            (outside / 'bin').mkdir()
            (outside / 'obj').mkdir()
            self.project.symlink_to(outside, target_is_directory=True)
            with self.assertRaises(ValueError):
                consumer.clean(self.root, 'samples/Maui/ChannelExplorer')
            self.assertTrue((outside / 'bin').exists())
            self.assertTrue((outside / 'obj').exists())

    def test_malformed_assets_abort_cleanup_planning(self):
        (self.project / 'obj/project.assets.json').write_text('incomplete restore')
        with self.assertRaises(json.JSONDecodeError):
            local.candidates(self.root, ['samples/Maui/ChannelExplorer/Sample.csproj'], builds=True)
        self.assertTrue((self.project / 'bin').exists())
