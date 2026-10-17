"""Detect downstream Core rebuilds and substitution of cached package bytes."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('shared_core', ROOT / '.github/scripts/shared-core.py')
shared = importlib.util.module_from_spec(spec)
spec.loader.exec_module(shared)


class SharedCoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.feed = self.root / 'artifacts/packages'
        self.feed.mkdir(parents=True)
        name = 'NativeChannels.Core.1.0.0.nupkg'
        archive = self.feed / name
        archive.write_bytes(b'original shared archive')
        (self.root / 'artifacts/shared-core.json').write_text(json.dumps({name: hashlib.sha256(archive.read_bytes()).hexdigest()}))
        self.cache = self.root / 'cache/nativechannels.core/1.0.0'
        self.cache.mkdir(parents=True)
        self.cached = self.cache / name.lower()
        self.cached.write_bytes(archive.read_bytes())
        for project in ('NativeChannels', 'NativeChannels.Maui'):
            obj = self.root / 'src' / project / 'obj'
            obj.mkdir(parents=True)
            (obj / 'project.assets.json').write_text(json.dumps({
                'libraries': {'NativeChannels.Core/1.0.0': {'type': 'package', 'path': 'nativechannels.core/1.0.0'}},
                'packageFolders': {str(self.root / 'cache'): {}}}))
        patcher = patch.object(shared, 'ROOT', self.root)
        patcher.start()
        self.addCleanup(patcher.stop)

    def test_exact_archive_passes(self):
        shared.verify(self.feed)

    def test_macos_does_not_require_a_maui_project(self):
        (self.root / 'src/NativeChannels.Maui/obj/project.assets.json').unlink()
        shared.verify(self.feed, 'macos')
        self.cached.write_bytes(b'stale macos cache')
        with self.assertRaisesRegex(ValueError, 'different Core bytes'):
            shared.verify(self.feed, 'macos')

    def test_stale_cache_is_rejected(self):
        self.cached.write_bytes(b'stale same-version archive')
        with self.assertRaisesRegex(ValueError, 'different Core bytes'):
            shared.verify(self.feed)

    def test_source_rebuild_is_rejected(self):
        (self.root / 'src/NativeChannels.Core/bin').mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, 'build outputs'):
            shared.verify(self.feed)

    def test_replaced_feed_archive_is_rejected(self):
        (self.feed / 'NativeChannels.Core.1.0.0.nupkg').write_bytes(b'rebuilt archive')
        with self.assertRaisesRegex(ValueError, 'bytes changed'):
            shared.verify(self.feed)

    def test_project_reference_is_rejected(self):
        assets = self.root / 'src/NativeChannels/obj/project.assets.json'
        content = json.loads(assets.read_text())
        content['libraries']['NativeChannels.Core/1.0.0']['type'] = 'project'
        assets.write_text(json.dumps(content))
        with self.assertRaisesRegex(ValueError, 'restore Core as a package'):
            shared.verify(self.feed)
