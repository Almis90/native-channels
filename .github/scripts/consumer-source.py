"""Allow current-run feeds only inside the distribution coordinator; keep external guards strict."""
import json
import os
import re

WORKFLOWS = {'Release': 'release-packages.yml', 'Distribution': 'verify-distribution.yml',
             'Example': 'build-examples.yml'}


def resolve(source, run_id, current_id, current_run, sha, repository):
    if source not in WORKFLOWS or not re.fullmatch(r'[1-9][0-9]*', run_id):
        raise ValueError('Choose Release, Distribution or Example and supply a numeric run ID.')
    current = run_id == str(current_id)
    if current and (source != 'Distribution'
                    or current_run.get('path') != '.github/workflows/verify-distribution.yml'
                    or current_run.get('event') != 'workflow_dispatch'
                    or current_run.get('head_sha') != sha
                    or (current_run.get('head_repository') or {}).get('full_name') != repository
                    or current_run.get('status') != 'in_progress'):
        raise ValueError('Only the active distribution coordinator may consume its own package feed.')
    return {'current': str(current).lower(), 'workflow': WORKFLOWS[source],
            'artifact': 'example-package-feed' if source == 'Example' else 'package-feed'}


if __name__ == '__main__':
    result = resolve(os.environ['PACKAGE_SOURCE'], os.environ['PACKAGE_RUN'], os.environ['GITHUB_RUN_ID'],
                     json.loads(os.environ['CURRENT_RUN']), os.environ['GITHUB_SHA'], os.environ['GITHUB_REPOSITORY'])
    with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
        for key, value in result.items():
            output.write(f'{key}={value}\n')
