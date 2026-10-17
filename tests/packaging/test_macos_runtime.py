"""A passing lookup cannot hide a crash, shutdown failure, or Catalyst binary."""
import importlib.util
from pathlib import Path
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('macos_proof', Path(__file__).resolve().parents[2] / 'eng/verify-currency-macos.py')
proof = importlib.util.module_from_spec(spec)
spec.loader.exec_module(proof)


class MacosRuntimeTests(unittest.TestCase):
    def test_result_requires_successful_exit_and_shutdown(self):
        result = 'CURRENCY_AVALONIA PASS apple: USD=$, EUR=€; normalization, errors, concurrency, cancellation\n'
        complete = result + 'CURRENCY_AVALONIA_CLOSED\n'
        self.assertIn('avalonia', proof.check_log(complete, 0))
        for log, code in ((result, 0), (complete, 1), (complete + result, 0),
                          (complete + 'CURRENCY_AVALONIA FAIL shutdown\n', 0)):
            with self.subTest(log=log, code=code), self.assertRaises(ValueError):
                proof.check_log(log, code)

    def test_native_platform_is_checked_independently_of_file_name(self):
        path = Path('Currency.app/Contents/MacOS/Currency')
        with patch.object(proof.subprocess, 'check_output', side_effect=[
            'arm64 x86_64', ' platform MACOS\n', 'AppKit.framework/AppKit\n',
        ]):
            self.assertEqual(['arm64', 'x86_64'], proof.check_binary(path, 'arm64')['architectures'])
        for build, dependencies in ((' platform MACCATALYST\n', ''),
                                    (' platform IOS\n', ''),
                                    (' platform MACOS\n', '/iOSSupport/UIKit.framework/UIKit')):
            with self.subTest(build=build, dependencies=dependencies), patch.object(
                proof.subprocess, 'check_output', side_effect=['arm64', build, dependencies]
            ), self.assertRaises(ValueError):
                proof.check_binary(path, 'arm64')


if __name__ == '__main__':
    unittest.main()
