"""Retain per-consumer step results and require all seven checks against one feed."""
import hashlib
import json
import os
from pathlib import Path
import sys

CONFIGURATIONS = {'android-x64': True, 'android-arm64': False, 'ios-simulator': True,
                  'ios-device': False, 'catalyst': False, 'win-x64': False, 'win-arm64': False}


def record(configuration, env, feed):
    if configuration not in CONFIGURATIONS:
        raise ValueError('Unknown consumer configuration')
    return {'configuration': configuration, 'sourceCommit': env['GITHUB_SHA'],
            'packageRun': env['PACKAGE_RUN'], 'packageSource': env['PACKAGE_SOURCE'], 'sample': env['SAMPLE'],
            'feedSha256': hashlib.sha256(feed.read_bytes()).hexdigest() if feed.exists() else None,
            'compiled': env.get('COMPILE_OUTCOME') == 'success',
            'executed': CONFIGURATIONS[configuration] and env.get('EXECUTION_OUTCOME') == 'success'}


def summarize(evidence, needs, sha, run_id):
    rows = {}
    for item in evidence:
        name = item['configuration']
        if name not in CONFIGURATIONS or name in rows:
            raise ValueError('Unexpected or duplicate consumer evidence')
        if (item.get('sourceCommit') != sha or str(item.get('packageRun')) != str(run_id)
                or item.get('packageSource') != 'Distribution' or item.get('sample') != 'Framework'):
            raise ValueError('Consumer evidence belongs to a different source, feed or sample')
        rows[name] = item
    hashes = {row.get('feedSha256') for row in rows.values()}
    failed = [name for name in ['packages', *CONFIGURATIONS]
              if needs.get(name, {}).get('result') != 'success']
    missing = [name for name, execution in CONFIGURATIONS.items()
               if rows.get(name, {}).get('compiled') is not True
               or rows[name].get('executed') is not execution]
    same_feed = len(rows) == len(CONFIGURATIONS) and len(hashes) == 1 and None not in hashes
    return {'sourceCommit': sha, 'runId': str(run_id), 'success': not failed and not missing and same_feed,
            'failedJobs': failed, 'unverifiedConsumers': missing, 'sameFeed': same_feed,
            'results': [rows[name] for name in CONFIGURATIONS if name in rows]}


def main():
    directory = Path('artifacts/consumer-evidence')
    directory.mkdir(parents=True, exist_ok=True)
    if len(sys.argv) == 3 and sys.argv[1] == 'record':
        item = record(sys.argv[2], os.environ, Path('artifacts/feed/package-feed.tar.gz'))
        (directory / f"{item['configuration']}.json").write_text(json.dumps(item, indent=2) + '\n')
        return
    if sys.argv[1:] != ['summarize']:
        raise ValueError('Usage: distribution-result.py record CONFIGURATION | summarize')
    evidence = [json.loads(path.read_text()) for path in directory.glob('*.json')]
    result = summarize(evidence, json.loads(os.environ['JOB_RESULTS']), os.environ['GITHUB_SHA'], os.environ['GITHUB_RUN_ID'])
    Path('artifacts/distribution-verification.json').write_text(json.dumps(result, indent=2) + '\n')
    with open(os.environ['GITHUB_STEP_SUMMARY'], 'a') as summary:
        summary.write('## Distribution verification\n\n| Consumer | Compiled | Executed |\n| --- | --- | --- |\n')
        rows = {row['configuration']: row for row in result['results']}
        for name in CONFIGURATIONS:
            row = rows.get(name, {})
            summary.write(f"| {name} | {row.get('compiled', False)} | {row.get('executed', False)} |\n")
        summary.write('\nOnly Android x64 and iOS simulator execute here. Physical devices, Catalyst runtime and Windows desktop execution are separate evidence. No packages were published.\n')
    if not result['success']:
        sys.exit('Distribution verification failed: missing/failed consumers or inconsistent package feed. See the evidence artifact.')


if __name__ == '__main__':
    main()
