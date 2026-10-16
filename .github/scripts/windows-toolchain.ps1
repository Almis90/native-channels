param(
    [Parameter(Mandatory)]
    [ValidateSet('Prepare', 'Install', 'Verify')]
    [string]$Phase
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $IsWindows -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') {
    throw 'The Windows CI toolchain requires a Windows x64 runner.'
}
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$pins = Get-Content -LiteralPath (Join-Path $repo 'global.json') -Raw | ConvertFrom-Json
$sdk = [string]$pins.sdk.version
$workload = [string]$pins.workload.version
foreach ($version in @($sdk, $workload)) {
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Pin explicit stable SDK and workload-set versions in global.json.' }
}
if (-not $env:RUNNER_TEMP) { throw 'RUNNER_TEMP is required.' }
$root = Join-Path $env:RUNNER_TEMP 'nativechannels-dotnet-windows'
$dotnet = Join-Path $root 'dotnet.exe'
$bandVersion = [Version]$sdk
$band = '{0}.{1}.{2}' -f $bandVersion.Major, $bandVersion.Minor, (100 * [Math]::Floor($bandVersion.Build / 100))

function Invoke-PinnedDotnet([string[]]$Arguments) {
    $result = & $dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Isolated dotnet failed: $($Arguments -join ' ')" }
    return $result
}

if ($Phase -eq 'Prepare') {
    if (-not $env:GITHUB_ENV -or -not $env:GITHUB_PATH -or -not $env:GITHUB_OUTPUT) {
        throw 'GitHub environment/output files are required.'
    }
    $image = if ($env:ImageOS) { $env:ImageOS } else { 'windows' }
    @("root=$root", "sdk=$sdk", "workload=$workload", "image=$image") | Add-Content -LiteralPath $env:GITHUB_OUTPUT
    @("DOTNET_ROOT=$root", "DOTNET_ROOT_X64=$root", "DOTNET_INSTALL_DIR=$root",
        'DOTNET_MULTILEVEL_LOOKUP=0', 'DOTNET_CLI_TELEMETRY_OPTOUT=1',
        'DOTNET_NOLOGO=1', 'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=true') | Add-Content -LiteralPath $env:GITHUB_ENV
    $root | Add-Content -LiteralPath $env:GITHUB_PATH
    return
}

if ($Phase -eq 'Install') {
    # Never reuse a system/MSI installation or an incomplete cache as an install destination.
    if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Force).Count) {
        throw "Expected an empty isolated SDK directory on cache miss: $root"
    }
    $installer = Join-Path $env:RUNNER_TEMP 'nativechannels-dotnet-install.ps1'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
    & $installer -Version $sdk -Architecture x64 -InstallDir $root -NoPath
    if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Isolated SDK installation failed.' }
    # ZIP SDKs use file-based workloads. MSI state belongs to the runner's Visual Studio install.
    if (Test-Path -LiteralPath (Join-Path $root "metadata/workloads/$band/installertype/msi")) {
        throw 'Unexpected MSI workload installer in the isolated SDK.'
    }
    Invoke-PinnedDotnet @('workload', 'install', 'maui-windows', '--version', $workload)
    return
}

if (-not (Test-Path -LiteralPath $dotnet)) { throw 'The restored toolchain has no dotnet.exe.' }
$resolved = (Get-Command dotnet -CommandType Application).Source
if ([IO.Path]::GetFullPath($resolved) -ne [IO.Path]::GetFullPath($dotnet)) {
    throw "PATH resolves to $resolved instead of the isolated SDK."
}
if ((Invoke-PinnedDotnet @('--version')).Trim() -ne $sdk) { throw 'SDK version differs from global.json.' }
if ((Invoke-PinnedDotnet @('workload', '--version')).Trim() -ne $workload) {
    throw 'Installed workload-set version differs from global.json.'
}
$inventory = ((Invoke-PinnedDotnet @('workload', 'list', '--machine-readable')) -join "`n") | ConvertFrom-Json
if (@($inventory.installed).Count -ne 1 -or $inventory.installed[0] -ne 'maui-windows') {
    throw "Unexpected installed workloads: $($inventory.installed -join ', ')"
}
$registration = Join-Path $root "metadata/workloads/$band/InstalledWorkloads/maui-windows"
if (-not (Test-Path -LiteralPath $registration)) { throw 'Missing file-based Windows workload registration.' }
$foreignPacks = @(Get-ChildItem -LiteralPath (Join-Path $root 'packs') -Directory | Where-Object {
    $_.Name -match '(?i)^Microsoft\.(Android|iOS|MacCatalyst|macOS|tvOS)\.|Emscripten|WebAssembly|Mono\.(android|ios|maccatalyst|browser)'
})
if ($foreignPacks.Count) { throw "Unexpected non-Windows workload packs: $($foreignPacks.Name -join ', ')" }
$record = @{
    sdk = $sdk; workloadSet = $workload; installed = @($inventory.installed)
    root = $root; cacheHit = ($env:TOOLCHAIN_CACHE_HIT -eq 'true'); foreignPlatformPacks = $foreignPacks.Count
}
$record | ConvertTo-Json -Depth 3 | Write-Output
if ($env:GITHUB_STEP_SUMMARY) {
    @('### Windows toolchain', '', "SDK: $sdk; workload set: $workload; installed: maui-windows.",
        "Exact cache hit: $($record.cacheHit). Non-Windows workload packs: $($foreignPacks.Count).",
        'Only the SDK directory is cached; consumer NuGet caches and build outputs remain separate.') |
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}
