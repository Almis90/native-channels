[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'Run this verifier in PowerShell 7 on Windows x64.'
}

$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/windows-interop'
$manifest = Join-Path $root 'native/windows/interop/rust/Cargo.toml'
$rustTarget = Join-Path $output 'cargo'
$rustVersion = '1.90.0'
New-Item -ItemType Directory -Path $output -Force | Out-Null

function Invoke-Checked([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command failed with exit code $LASTEXITCODE." }
}

# Import the installed MSVC x64 environment without starting a visible shell.
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ build tools and a Windows SDK.' }
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'No Visual Studio C++ x64 tools found.' }
    Import-Module (Join-Path $installation 'Common7/Tools/Microsoft.VisualStudio.DevShell.dll')
    Enter-VsDevShell -VsInstallPath $installation -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64'
}
if ($env:VSCMD_ARG_TGT_ARCH -ne 'x64') { throw 'Use an x64 MSVC developer environment.' }
Get-Command cargo.exe, rustup.exe, dotnet.exe, cl.exe -ErrorAction Stop | Out-Null

Push-Location $root
try {
    # All outputs stay under artifacts; no third-party Rust crates or MAUI workloads are needed.
    Invoke-Checked 'cargo' @("+$rustVersion", 'fmt', '--manifest-path', $manifest, '--', '--check')
    Invoke-Checked 'cargo' @("+$rustVersion", 'build', '--locked', '--offline', '--release', '--target', 'x86_64-pc-windows-msvc', '--manifest-path', $manifest, '--target-dir', $rustTarget)
    Copy-Item -LiteralPath (Join-Path $rustTarget 'x86_64-pc-windows-msvc/release/native_channels_rust_probe.dll') -Destination $output
    Invoke-Checked 'cl.exe' @('/nologo', '/std:c++17', '/W4', '/WX', '/EHsc', '/MT', '/LD', '/O2',
        (Join-Path $root 'native/windows/interop/cpp/probe.cpp'),
        "/Fo$(Join-Path $output 'probe.obj')", "/Fe$(Join-Path $output 'native_channels_cpp_probe.dll')",
        '/link', 'kernel32.lib', "/IMPLIB:$(Join-Path $output 'native_channels_cpp_probe.lib')")

    $project = Join-Path $root 'tests/NativeChannels.Windows.Interop/NativeChannels.Windows.Interop.csproj'
    Invoke-Checked 'dotnet' @('build', $project, '-c', 'Release')
    $log = Join-Path $output 'verification.log'
    & dotnet run --project $project -c Release --no-build -- $output 2>&1 | Tee-Object -FilePath $log
    if ($LASTEXITCODE -ne 0) { throw 'Native DLL channel verification failed.' }
    $hashes = @(Get-ChildItem -LiteralPath $output -Filter '*.dll' | Get-FileHash -Algorithm SHA256 |
        Select-Object @{Name='file';Expression={Split-Path $_.Path -Leaf}}, Hash)
    $sourceCommit = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source commit.' }
    @{
        sourceCommit = $sourceCommit
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = 'x64'
        rust = (& rustc "+$rustVersion" --version)
        msvc = $env:VCToolsVersion
        dotnet = (& dotnet --version)
        status = 'passed'
        dlls = $hashes
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'verification.json')
} finally {
    Pop-Location
}
