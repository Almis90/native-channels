#!/usr/bin/env python3
"""Wait for a terminal sample result, including files still being written."""
import argparse
from pathlib import Path
import re
import sys
import time


def read_result(paths, success):
    for path in paths:
        try:
            text = path.read_text(encoding='utf-8', errors='replace')
        except FileNotFoundError:
            continue
        if text.startswith('FAIL'):
            return False, text
        if re.search(success, text):
            return True, text
    return None, ''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--timeout', required=True, type=float)
    parser.add_argument('--success', required=True)
    parser.add_argument('--logs', nargs=2, type=Path, required=True)
    parser.add_argument('results', nargs=2, type=Path)
    args = parser.parse_args()
    deadline = time.monotonic() + args.timeout
    while True:
        passed, result = read_result(args.results, args.success)
        if passed is not None:
            print(result, flush=True)
            if passed:
                return 0
            break
        if time.monotonic() >= deadline:
            print(f'FAIL: No complete sample result within {args.timeout:g} seconds', file=sys.stderr)
            for path in args.results:
                if path.exists():
                    print(f'{path}: {path.read_text(errors="replace")!r}', file=sys.stderr)
            break
        time.sleep(min(1, max(0, deadline - time.monotonic())))
    for path in args.logs:
        if path.exists():
            print(f'--- {path.name} ---\n{path.read_text(errors="replace")}', file=sys.stderr)
    return 1


if __name__ == '__main__':
    sys.exit(main())
