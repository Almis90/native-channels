"""Contract tests for the local runner: no desktop/device required."""
import importlib.util
import sys
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).parents[1]))
SPEC = importlib.util.spec_from_file_location('local_samples', Path(__file__).parents[1] / 'test-samples.py')
runner = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(runner)


class LocalSamplesTests(unittest.TestCase):
    def test_candidate_consumers_never_repack_framework_archives(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            args = SimpleNamespace(platform='windows', framework='maui', dotnet='dotnet',
                                   reuse_external_cache=None, arch='x64', framework_dependent=False,
                                   framework_packages=root / 'candidate')
            identity = dict(commit='a' * 40, status='', sourceSha256='b' * 64)
            with patch.object(runner, 'command') as command, \
                 patch.object(runner, 'source_identity', return_value=identity), \
                 patch.object(runner, 'verify_candidate', return_value={'files': []}) as verify:
                runner.build(args, root / 'build')
            packs = [call.args[0] for call in command.call_args_list if 'pack' in call.args[0]]
            self.assertEqual(len(packs), 4)
            self.assertTrue(all(str(call[2]).startswith('plugins/') for call in packs))
            self.assertEqual(verify.call_count, 2)

    def test_dirty_candidate_build_fails_before_running_tools(self):
        args = SimpleNamespace(require_clean=True)
        with patch.object(runner, 'source_identity', return_value={'status': ' M source.cs'}), \
             patch.object(runner, 'command') as command:
            with self.assertRaisesRegex(ValueError, 'clean committed'):
                runner.build(args, Path('unused'))
            command.assert_not_called()

    def test_apple_builds_runnable_bundles_in_manifest_directories(self):
        for framework, target in (('avalonia', 'macos'), ('avalonia', 'ios'),
                                  ('maui', 'maccatalyst'), ('maui', 'ios')):
            with self.subTest(framework=framework, target=target), tempfile.TemporaryDirectory() as temporary:
                args = SimpleNamespace(platform=target, framework=framework, dotnet='dotnet',
                                       reuse_external_cache=None, arch='arm64')
                with patch.object(runner, 'command') as command, \
                     patch.object(runner, 'source_identity', return_value={}):
                    manifest_path = runner.build(args, Path(temporary))
                import json
                manifest = json.loads(manifest_path.read_text())
                consumers = [call.args[0] for call in command.call_args_list
                             if any(str(arg).endswith('.csproj') for arg in call.args[0])]
                self.assertEqual(len(consumers), 5)
                for invocation, app in zip(consumers, manifest['apps']):
                    self.assertEqual(invocation[1], 'build')
                    self.assertEqual(str(invocation[invocation.index('-o') + 1]), app['directory'])
                    self.assertIn('Release', invocation)
                    self.assertIn(('iossimulator-' if target == 'ios' else
                                   'maccatalyst-' if target == 'maccatalyst' else 'osx-') + 'arm64', invocation)

    def test_success_requires_process_exit_and_cleanup(self):
        runner.validate_result('Currency', 'PASS native checks', 'CURRENCY_AVALONIA_CLOSED\n')
        for text, log, code in [('PASS native checks', '', 0), ('PASS native checks', 'CURRENCY_AVALONIA_CLOSED', 1),
                                ('', 'CURRENCY_AVALONIA_CLOSED', 0), ('FAIL native check', 'CURRENCY_AVALONIA_CLOSED', 0),
                                ('PASS native checks', 'CURRENCY_AVALONIA_CLOSED\nCURRENCY_AVALONIA_CLOSED', 0)]:
            with self.subTest(text=text, code=code), self.assertRaises(RuntimeError):
                runner.validate_result('Currency', text, log, code)

    def test_legacy_audio_requires_cleanup(self):
        with self.assertRaises(RuntimeError):
            runner.validate_result('AudioStreamer', 'PASS PCM', '')
        runner.validate_result('AudioStreamer', 'PASS PCM', 'AUDIO_AVALONIA_CLOSED')
        runner.validate_result('AudioStreamer', 'PASS PCM', 'NATIVECHANNELS_LOCAL_RESULT PASS PCM')

    def test_desktop_cleanup_checks_both_output_streams(self):
        marker = 'NATIVECHANNELS_MAUI_CLOSED'
        for stdout, stderr, succeeds in (('', marker, True), (marker, '', True),
                                          ('', '', False), (marker, marker, False)):
            with self.subTest(stdout=stdout, stderr=stderr), tempfile.TemporaryDirectory() as temporary:
                folder = Path(temporary)

                def launch(*args, **kwargs):
                    kwargs['stdout'].write(stdout)
                    kwargs['stderr'].write(stderr)
                    (folder / 'result.txt').write_text('PASS native checks')
                    return SimpleNamespace(wait=lambda **kwargs: 0, poll=lambda: 0)

                entry = dict(sample='Currency', name='Currency', framework='maui', directory=temporary)
                with patch.object(runner.subprocess, 'Popen', side_effect=launch):
                    if succeeds:
                        self.assertEqual(runner.desktop(entry, 'windows', folder, 1), 'PASS native checks')
                    else:
                        with self.assertRaisesRegex(RuntimeError, 'one completed MAUI cleanup marker'):
                            runner.desktop(entry, 'windows', folder, 1)

    def test_timeout_kills_owned_process(self):
        import subprocess
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            with patch.object(runner.subprocess, 'Popen') as launch:
                process = launch.return_value
                process.wait.side_effect = [subprocess.TimeoutExpired('sample', 1), 0]
                process.poll.return_value = None
                with self.assertRaises(subprocess.TimeoutExpired):
                    runner.desktop(dict(sample='Interop', name='Interop', directory=temporary), 'windows', folder, 1)
                process.kill.assert_called_once()

    def test_project_matrix_includes_both_pdf_backends(self):
        for target in (target for target in runner.HEADS if target != 'maccatalyst'):
            for sample in runner.SAMPLES:
                for backend in (('Qt', 'Gtk') if target == 'linux' and sample == 'PdfViewer' else ('',)):
                    self.assertTrue(runner.project(sample, target, backend).is_file())

    def test_maui_project_matrix(self):
        for target in ('windows', 'android', 'ios', 'maccatalyst'):
            for sample in runner.SAMPLES:
                self.assertTrue(runner.project(sample, target, framework='maui').is_file())
        self.assertEqual(runner.project('Interop', 'windows', framework='maui').stem, 'ChannelExplorer')

    def test_rejects_unsupported_framework_platform_before_building(self):
        import subprocess
        import sys
        for framework, target in (('maui', 'linux'), ('maui', 'browser'), ('avalonia', 'maccatalyst')):
            result = subprocess.run([sys.executable, str(Path(runner.__file__)), '--framework', framework,
                                     '--platform', target], capture_output=True, text=True)
            self.assertEqual(result.returncode, 2)
            self.assertIn(framework + ' supports:', result.stderr)

    def test_maui_requires_cleanup_even_with_a_pass_result(self):
        for sample in runner.SAMPLES:
            runner.validate_result(sample, 'PASS native checks', 'NATIVECHANNELS_MAUI_CLOSED', framework='maui')
            for log in ('', 'NATIVECHANNELS_MAUI_CLOSED NATIVECHANNELS_MAUI_CLOSED'):
                with self.subTest(sample=sample, log=log), self.assertRaises(RuntimeError):
                    runner.validate_result(sample, 'PASS native checks', log, framework='maui')

    def test_virtual_audio_restores_environment_and_unloads_on_failure(self):
        with patch.dict(runner.os.environ, {'PULSE_SINK': 'original'}), \
             patch.object(runner, 'capture', return_value='123'), patch.object(runner, 'command') as command:
            with self.assertRaisesRegex(RuntimeError, 'sample failed'):
                with runner.audio_environment(SimpleNamespace(platform='linux', virtual_audio=True)):
                    self.assertTrue(runner.os.environ['PULSE_SINK'].startswith('nativechannels_test_'))
                    raise RuntimeError('sample failed')
            self.assertEqual(runner.os.environ['PULSE_SINK'], 'original')
            command.assert_called_once_with(['pactl', 'unload-module', '123'])


if __name__ == '__main__':
    unittest.main()
