param([string]$Dotnet = '', [string]$ReuseCache = '', [switch]$BuildOnly)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $Dotnet) { $Dotnet = Join-Path $repo '.tools/dotnet/dotnet.exe' }
$Dotnet = (Resolve-Path -LiteralPath $Dotnet).Path
$output = Join-Path $repo ('artifacts/uno-samples/' + [Guid]::NewGuid().ToString('N'))
$cache = if ($ReuseCache) { (Resolve-Path -LiteralPath $ReuseCache).Path } else { Join-Path $output 'cache' }
New-Item -ItemType Directory -Force $output | Out-Null
$config = Join-Path $repo 'samples/Uno/NuGet.Config'
$previousRoot = $env:DOTNET_ROOT
$previousResult = $env:NATIVECHANNELS_UNO_SAMPLE_RESULT
Push-Location (Join-Path $repo 'samples/Uno')
try {
    $env:DOTNET_ROOT = Split-Path $Dotnet
    foreach ($sample in @('Interop', 'Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer')) {
        $project = Join-Path $repo "samples/Uno/$sample/$sample.csproj"
        & $Dotnet build $project -c Release '-p:TargetFrameworks=net10.0-desktop' "-p:RestorePackagesPath=$cache" --configfile $config *> (Join-Path $output "$sample-build.log")
        if ($LASTEXITCODE -ne 0) { throw "$sample build failed. See $output" }
        Copy-Item -LiteralPath (Join-Path (Split-Path $project) 'obj/project.assets.json') -Destination (Join-Path $output "$sample-assets.json")
        if (-not $BuildOnly -and $env:OS -eq 'Windows_NT') {
            $binary = Join-Path (Split-Path $project) "bin/Release/net10.0-desktop/$sample.exe"
            $env:NATIVECHANNELS_UNO_SAMPLE_RESULT = Join-Path $output "$sample.json"
            $process = Start-Process -FilePath $binary -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $output "$sample-stdout.log") `
                -RedirectStandardError (Join-Path $output "$sample-stderr.log")
            if (-not $process.WaitForExit(35000)) {
                Stop-Process -Id $process.Id
                throw "$sample timed out. See $output"
            }
            if (-not (Test-Path -LiteralPath $env:NATIVECHANNELS_UNO_SAMPLE_RESULT)) { throw "$sample produced no result. See $output" }
            $result = Get-Content -Raw -LiteralPath $env:NATIVECHANNELS_UNO_SAMPLE_RESULT | ConvertFrom-Json
            if ($result.checks -ne 'passed') { throw "$sample checks failed: $($result.error)" }
            Write-Output "$sample checks passed. $($result.limitation)"
        }
    }
    Get-ChildItem -LiteralPath (Join-Path $repo 'artifacts/uno-feasibility/packages') -Filter '*.nupkg' |
        Get-FileHash -Algorithm SHA256 | Select-Object Path, Hash | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $output 'packages.json') -Encoding utf8
    @{ freshCache = -not [bool]$ReuseCache; cache = $cache; buildOnly = [bool]$BuildOnly } | ConvertTo-Json |
        Set-Content -LiteralPath (Join-Path $output 'restore-mode.json') -Encoding utf8
    Write-Output "Evidence: $output"
    Write-Output 'This is not six-platform acceptance; other-platform PDF adapters and broader visual/input review remain pending.'
}
finally {
    $env:DOTNET_ROOT = $previousRoot
    $env:NATIVECHANNELS_UNO_SAMPLE_RESULT = $previousResult
    Pop-Location
}
