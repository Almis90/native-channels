"""Install and verify a single file-based MAUI workload without using the host SDK."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess

REPO = Path(__file__).resolve().parents[2]
TARGETS = {'android', 'ios', 'maccatalyst'}


def versions(repo):
    pins = json.loads((repo / 'global.json').read_text())
    sdk, workload = pins['sdk']['version'], pins['workload']['version']
    if any(not re.fullmatch(r'\d+\.\d+\.\d+', version) for version in (sdk, workload)):
        raise ValueError('Pin explicit stable SDK and workload-set versions in global.json.')
    major, minor, patch = map(int, sdk.split('.'))
    return sdk, workload, f'{major}.{minor}.{patch // 100 * 100}'


def host(target):
    system = platform.system()
    if system not in {'Linux', 'Darwin'} or (target != 'android' and system != 'Darwin'):
        raise ValueError('Apple workloads require macOS; Android requires Linux or macOS.')
    architecture = {'x86_64': 'x64', 'AMD64': 'x64', 'aarch64': 'arm64', 'arm64': 'arm64'}.get(platform.machine())
    if not architecture:
        raise ValueError('Unsupported SDK host architecture.')
    return architecture


def append_env_file(name, values):
    with open(os.environ[name], 'a', encoding='utf-8') as output:
        output.write('\n'.join(values) + '\n')


def prepare(root, target, sdk, workload, architecture):
    xcode = 'none'
    if target != 'android':
        version = subprocess.check_output(['xcodebuild', '-version'], text=True).strip()
        xcode = hashlib.sha256(version.encode()).hexdigest()[:16]
        print(version)
    append_env_file('GITHUB_OUTPUT', [f'root={root}', f'target={target}', f'sdk={sdk}',
                                    f'workload={workload}', f'image={os.environ.get("ImageOS", platform.system())}',
                                    f'xcode={xcode}'])
    append_env_file('GITHUB_ENV', [f'TOOLCHAIN_TARGET={target}', f'DOTNET_ROOT={root}',
                                 f'DOTNET_ROOT_{architecture.upper()}={root}', f'DOTNET_INSTALL_DIR={root}',
                                 'DOTNET_MULTILEVEL_LOOKUP=0', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
                                 'DOTNET_NOLOGO=1', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true'])
    append_env_file('GITHUB_PATH', [str(root)])


def install(root, target, sdk, workload, architecture):
    # Do not merge a partial cache or another target's SDK into this installation.
    if root.exists() and any(root.iterdir()):
        raise ValueError(f'Expected an empty private SDK directory on cache miss: {root}')
    installer = Path(os.environ['RUNNER_TEMP']) / 'nativechannels-dotnet-install.sh'
    subprocess.run(['curl', '--fail', '--location', '--proto', '=https',
                    'https://dot.net/v1/dotnet-install.sh', '-o', str(installer)], check=True)
    subprocess.run(['bash', str(installer), '--version', sdk, '--architecture', architecture,
                    '--install-dir', str(root), '--no-path'], check=True)
    subprocess.run([str(root / 'dotnet'), 'workload', 'install', f'maui-{target}',
                    '--version', workload], check=True, cwd=REPO)


def foreign_packs(root, target):
    foreign = []
    for pack in (root / 'packs').iterdir():
        if not pack.is_dir():
            continue
        name = pack.name.lower()
        platforms = {value for value in TARGETS | {'tvos', 'macos', 'browser'}
                     if re.search(r'(?:^|[.-])' + value + r'(?:$|[.-])', name)}
        if platforms - {target} or any(value in name for value in ('emscripten', 'webassembly', 'winui', 'windowsdesktop')):
            foreign.append(pack.name)
    return sorted(foreign)


def verify(root, target, sdk, workload, band):
    dotnet = root / 'dotnet'
    resolved = shutil.which('dotnet')
    if not dotnet.is_file() or not resolved or Path(resolved).resolve() != dotnet.resolve():
        raise ValueError(f'PATH resolves to {resolved} instead of the private SDK {dotnet}.')

    def query(*args):
        return subprocess.check_output([str(dotnet), *args], text=True, cwd=REPO).strip()

    if query('--version') != sdk:
        raise ValueError('SDK version differs from global.json.')
    if query('workload', '--version') != workload:
        raise ValueError('Installed workload-set version differs from global.json.')
    inventory = json.loads(query('workload', 'list', '--machine-readable'))
    if inventory['installed'] != [f'maui-{target}']:
        raise ValueError(f'Unexpected installed workloads: {inventory["installed"]}')
    metadata = root / 'metadata' / 'workloads' / band
    if not (metadata / 'InstalledWorkloads' / f'maui-{target}').is_file():
        raise ValueError('Missing file-based workload registration.')
    if (metadata / 'installertype' / 'msi').exists():
        raise ValueError('Unexpected MSI workload registration.')
    foreign = foreign_packs(root, target)
    if foreign:
        raise ValueError(f'Unexpected unrelated workload packs: {foreign}')
    return {'sdk': sdk, 'workloadSet': workload, 'installed': inventory['installed'],
            'root': str(root), 'cacheHit': os.environ.get('TOOLCHAIN_CACHE_HIT') == 'true',
            'foreignPlatformPacks': len(foreign)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('phase', choices=('prepare', 'install', 'verify'))
    args = parser.parse_args()
    target = os.environ['TOOLCHAIN_TARGET']
    if target not in TARGETS:
        raise ValueError(f'Unsupported workload target: {target}')
    architecture = host(target)
    sdk, workload, band = versions(REPO)
    # eng/dotnet.sh uses this same private path, including during fresh-cache consumer builds.
    root = REPO / '.tools' / 'dotnet'
    if root.is_symlink():
        raise ValueError('The private SDK directory must not link to a host installation.')
    if args.phase == 'prepare':
        prepare(root, target, sdk, workload, architecture)
    elif args.phase == 'install':
        install(root, target, sdk, workload, architecture)
    else:
        record = verify(root, target, sdk, workload, band)
        print(json.dumps(record, indent=2))
        if os.environ.get('GITHUB_STEP_SUMMARY'):
            append_env_file('GITHUB_STEP_SUMMARY', [f'### {target} toolchain', '',
                f'SDK: {sdk}; workload set: {workload}; installed: maui-{target}.',
                f'Exact cache hit: {record["cacheHit"]}. Unrelated workload packs: 0.',
                'Only the SDK directory is cached; consumer NuGet caches and build outputs remain separate.'])


if __name__ == '__main__':
    main()
