"""Package inputs for Uno development feeds and a framework-compatible release."""

PLATFORMS = ('windows', 'linux', 'macos', 'android', 'ios', 'browser')
COMPATIBILITY_PLATFORMS = ('windows-native', 'maccatalyst', 'macos-native')
SHARED = {'NativeChannels.Core', 'NativeChannels', 'Plugin.Currency',
          'Plugin.AudioStreamer', 'Plugin.VolumeMonitor'}
PACKAGES = SHARED | {'NativeChannels.Uno', 'Plugin.Uno.PdfViewer'}


def expected_packages(platform, include_existing_hosts=False, frameworks_only=False):
    packages = set(PACKAGES if platform in PLATFORMS else SHARED)
    if platform == 'android':
        packages.add('NativeChannels.Native.Android')
    if platform in ('ios', 'maccatalyst', 'macos-native'):
        packages.add('NativeChannels.Native.Apple')
    if include_existing_hosts:
        if platform in ('android', 'ios', 'windows-native', 'maccatalyst'):
            packages.update(('NativeChannels.Maui', 'Plugin.Maui.PdfViewer'))
        if platform in ('android', 'ios', 'windows-native', 'macos-native', 'linux', 'browser'):
            packages.update(('NativeChannels.Avalonia', 'Plugin.Avalonia.PdfViewer'))
    return {name for name in packages if not name.startswith("Plugin.")} if frameworks_only else packages


def validate_release_targets(records, frameworks_only=False):
    """A release must not silently drop the pre-Uno consumer target frameworks."""
    required = {
        'NativeChannels': ('net10.0', 'net10.0-android', 'net10.0-ios', 'net10.0-maccatalyst', 'net10.0-macos', 'net10.0-windows', 'net10.0-browser'),
        'NativeChannels.Native.Apple': ('net10.0-ios', 'net10.0-maccatalyst', 'net10.0-macos'),
        'NativeChannels.Maui': ('net10.0-android', 'net10.0-ios', 'net10.0-maccatalyst', 'net10.0-windows'),
        'NativeChannels.Avalonia': ('net10.0',),
        'Plugin.Maui.PdfViewer': ('net10.0-android', 'net10.0-ios', 'net10.0-maccatalyst', 'net10.0-windows'),
        'Plugin.Avalonia.PdfViewer': ('net10.0', 'net10.0-android', 'net10.0-ios', 'net10.0-macos', 'net10.0-windows', 'net10.0-browser'),
    }
    for name in ('Plugin.Currency', 'Plugin.AudioStreamer', 'Plugin.VolumeMonitor'):
        required[name] = required['NativeChannels']
    by_id = {record['id']: record for record in records}
    for name, targets in required.items():
        if frameworks_only and name.startswith('Plugin.'):
            continue
        frameworks = {path.split('/')[1] for path in by_id.get(name, {}).get('assemblies', [])}
        for target in targets:
            if not any(tfm == target or (target != 'net10.0' and tfm.startswith(target) and tfm[len(target):len(target)+1].isdigit()) for tfm in frameworks):
                raise ValueError(f'{name}: missing existing consumer target {target}')
