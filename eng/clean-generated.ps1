[CmdletBinding()]
param(
    [switch]$DryRun,
    [switch]$IncludeDependencies
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$repoPrefix = $repo.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$comparison = [StringComparison]::OrdinalIgnoreCase

function Get-GeneratedDirectory([string]$RelativePath) {
    $path = [IO.Path]::GetFullPath((Join-Path $repo $RelativePath))
    if (-not $path.StartsWith($repoPrefix, $comparison)) {
        throw "Refusing to clean a path outside the repository: $path"
    }
    if (Test-Path -LiteralPath $path -PathType Container) { return $path }
}

# Use tracked project files so a large generated tree is never searched recursively.
$projects = & git -C $repo ls-files -- '*.csproj'
if ($LASTEXITCODE -ne 0) { throw 'Could not list project files with git.' }

$targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($project in $projects) {
    $directory = Split-Path $project -Parent
    foreach ($name in @('bin', 'obj')) {
        [void]$targets.Add((Join-Path $directory $name))
    }
}

$gradleProjects = & git -C $repo ls-files -- 'build.gradle' 'build.gradle.kts' '**/build.gradle' '**/build.gradle.kts'
if ($LASTEXITCODE -ne 0) { throw 'Could not list Gradle projects with git.' }
foreach ($project in $gradleProjects) {
    [void]$targets.Add((Join-Path (Split-Path $project -Parent) 'build'))
}

foreach ($path in @('artifacts', 'native/android/.gradle', 'native/windows/interop/rust/target',
        'benchmarks/NativeChannels.Benchmarks/BenchmarkDotNet.Artifacts',
        'docs/.vitepress/cache', 'docs/.vitepress/dist', 'docs/.vitepress/.temp')) {
    [void]$targets.Add($path)
}

if ($IncludeDependencies) {
    foreach ($path in @('.tools', 'node_modules', 'tests/NativeChannels.Browser.Tests/node_modules',
            'plugins/Plugin.Avalonia.PdfViewer/node_modules')) {
        [void]$targets.Add($path)
    }
}

$existing = @($targets | Sort-Object | ForEach-Object { Get-GeneratedDirectory $_ })
if ($DryRun) {
    foreach ($path in $existing) { Write-Host "Would remove: $path" }
    return
}

# Large roots contain independent output folders. Delete those concurrently, then remove the empty root.
$splitRoots = @($existing | Where-Object {
    $_ -eq (Join-Path $repo 'artifacts') -or ($IncludeDependencies -and $_ -eq (Join-Path $repo '.tools'))
})
$work = [Collections.Generic.List[string]]::new()
foreach ($path in $existing) {
    if ($path -in $splitRoots) {
        foreach ($item in [IO.Directory]::EnumerateFileSystemEntries($path)) {
            if (-not $item.StartsWith(($path.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar), $comparison)) {
                throw "Refusing to clean a path outside the output folder: $item"
            }
            $work.Add($item)
        }
    } else {
        $work.Add($path)
    }
}

if ($work.Count -gt 0) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading.Tasks;

public static class NativeChannelsCleaner {
    public static void Delete(string[] paths) {
        Parallel.ForEach(paths, new ParallelOptions { MaxDegreeOfParallelism = 4 }, path => {
            Console.WriteLine("Removing: " + path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        });
    }
}
'@
    [NativeChannelsCleaner]::Delete($work.ToArray())
}
foreach ($path in $splitRoots) {
    [IO.Directory]::Delete($path, $false)
}
