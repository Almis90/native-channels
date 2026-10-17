"""Linux runtime evidence requires all scenarios, successful exit, and completed disposal."""
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('linux_proof', Path(__file__).resolve().parents[2] / 'eng/verify-currency-linux.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


class LinuxRuntimeTests(unittest.TestCase):
    def test_pass_cannot_hide_failed_shutdown_or_incomplete_results(self):
        result = 'CURRENCY_AVALONIA PASS linux: USD=$, EUR=€, GBP=£, JPY=¥; normalization, errors, concurrency, cancellation\n'
        complete = result + 'CURRENCY_AVALONIA_CLOSED\n'
        self.assertIn('avalonia', proof.check_log(complete, 0))
        for log, code in ((result, 0), (complete, 1), (complete + result, 0),
                          (complete.replace('linux:', 'apple:'), 0),
                          (complete.replace(', cancellation', ''), 0),
                          (complete + 'CURRENCY_AVALONIA_CLOSED\n', 0)):
            with self.subTest(log=log, code=code), self.assertRaises(ValueError):
                proof.check_log(log, code)
