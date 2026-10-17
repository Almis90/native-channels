import importlib.util
from pathlib import Path
import sys
import unittest

ENG = Path(__file__).resolve().parents[2] / 'eng'
sys.path.insert(0, str(ENG))
spec = importlib.util.spec_from_file_location('audio_linux', ENG / 'verify-audio-avalonia-linux.py')
audio = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audio)


class AudioLinuxRuntimeTests(unittest.TestCase):
    def test_requires_completed_shutdown_and_full_host_checks(self):
        result = 'PASS native PCM; host recreation, controls and minimize/restart'
        log = 'AUDIO_AVALONIA ' + result + '\nAUDIO_AVALONIA_CLOSED\n'
        audio.check_result(log, result, 0)
        for invalid_log, invalid_result, code in (
            (log, result, 1), (log, 'PASS native PCM', 0),
            (log.replace('AUDIO_AVALONIA_CLOSED\n', ''), result, 0),
            (log + 'AUDIO_AVALONIA_CLOSED\n', result, 0),
            (log + 'AUDIO_AVALONIA FAIL shutdown\n', result, 0),
        ):
            with self.subTest(code=code, log=invalid_log), self.assertRaises(ValueError):
                audio.check_result(invalid_log, invalid_result, code)
