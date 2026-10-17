param(
    [string]$PackageFeed,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$BuildOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$dotnet = if (Test-Path (Join-Path $repoRoot '.tools/dotnet/dotnet.exe')) { Join-Path $repoRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
$nativeRuntime = 'win-' + [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
if (-not $BuildOnly -and $RuntimeIdentifier -ne $nativeRuntime) {
    throw 'Runtime verification requires the native OS architecture. Use BuildOnly for cross-compilation.'
}
$runRoot = Join-Path $repoRoot ('artifacts/shared-hosting-windows/' + $RuntimeIdentifier + '/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function Invoke-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Invoke-Python {
    & python @args
    if ($LASTEXITCODE -ne 0) { throw "Python verification failed with exit code $LASTEXITCODE" }
}

function Invoke-Host([string]$HostName, [string]$Executable, [string]$ResultVariable) {
    $destination = Join-Path $runRoot "$HostName-raw.txt"
    $previous = [Environment]::GetEnvironmentVariable($ResultVariable)
    $process = $null
    try {
        [Environment]::SetEnvironmentVariable($ResultVariable, $destination)
        $start = @{ FilePath = $Executable; PassThru = $true; WindowStyle = 'Hidden' }
        if ($HostName -eq 'avalonia') { $start.ArgumentList = '--verify' }
        $process = Start-Process @start
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        $report = ''
        while (-not $report) {
            if (Test-Path -LiteralPath $destination) {
                try { $report = [IO.File]::ReadAllText($destination) } catch [IO.IOException] { }
                if ($report) { break }
            }
            if ($process.HasExited) { throw "$HostName exited before writing its result (exit $($process.ExitCode))." }
            if ([DateTime]::UtcNow -gt $deadline) { throw "$HostName runtime verification timed out." }
            Start-Sleep -Milliseconds 200
        }
        if ($HostName -eq 'avalonia') {
            # Verification mode closes through the normal window-closing handler, awaiting disposal.
            if (-not $process.WaitForExit(10000)) { throw 'Avalonia did not finish window/session cleanup.' }
            # Cleanup can replace an earlier PASS with FAIL, so read again after process exit.
            $report = [IO.File]::ReadAllText($destination)
            if ($process.ExitCode -ne 0) { throw "Avalonia exited with $($process.ExitCode): $report" }
        }
        $marker = if ($HostName -eq 'maui') { 'CURRENCY_SAMPLE' } else { 'CURRENCY_AVALONIA' }
        [IO.File]::WriteAllText((Join-Path $runRoot "$HostName-result.txt"), "$marker $report")
        Write-Output "$marker $report"
    }
    finally {
        [Environment]::SetEnvironmentVariable($ResultVariable, $previous)
        if ($process -and -not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id }
        }
    }
}

Push-Location $repoRoot
try {
    if (-not $PackageFeed) {
        & ./.github/scripts/pack-windows.ps1 -Scope Framework
        & ./.github/scripts/pack-windows.ps1 -Scope Plugin -Plugin Currency
        $PackageFeed = 'artifacts/windows/packages'
    }
    $feed = (Resolve-Path -LiteralPath $PackageFeed).Path
    Invoke-Python eng/clean-consumer.py samples/Currency.Sample
    Invoke-Python eng/clean-consumer.py samples/Currency.Avalonia.Windows
    $mauiCache = Join-Path $repoRoot ('.tools/wn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $avaloniaCache = Join-Path $repoRoot ('.tools/wn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
    $mauiRoot = Join-Path $runRoot 'maui'
    & ./eng/verify-windows.ps1 -Currency -PackageFeed $feed -WindowsOnlyFeed -BuildOnly `
        -RuntimeIdentifier $RuntimeIdentifier -PackageCache $mauiCache -VerificationDirectory $mauiRoot

    $project = 'samples/Currency.Avalonia.Windows/Currency.Avalonia.Windows.csproj'
    $app = Join-Path $runRoot 'avalonia/app'
    $settings = @("-p:RestorePackagesPath=$avaloniaCache", "-p:RuntimeIdentifier=$RuntimeIdentifier", '-p:SelfContained=true', '-p:Configuration=Release')
    Invoke-Dotnet restore $project @settings --configfile (Join-Path $mauiRoot 'NuGet.Config') --force --no-http-cache
    $proof = @('--platform', 'windows', '--runtime', $RuntimeIdentifier, '--feed', $feed, '--output', $runRoot)
    Invoke-Python eng/verify-shared-hosting.py packages @proof
    Invoke-Dotnet publish $project @settings -c Release --no-restore -o $app

    $source = (& git rev-parse HEAD).Trim()
    if ($env:GITHUB_SHA) { $source = $env:GITHUB_SHA }
    [ordered]@{
        sourceCommit = $source
        mode = 'compile'
        platform = 'windows'
        runtimeIdentifier = $RuntimeIdentifier
        configuration = 'Release'
        deployment = 'unpackaged'
        hosts = @('maui', 'avalonia')
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runRoot 'compilation.json') -Encoding utf8
    if ($BuildOnly) {
        Write-Output "COMPILE ONLY: both Windows Currency hosts built; evidence at $runRoot"
        return
    }

    Invoke-Host 'maui' (Join-Path $mauiRoot 'app/Currency.Sample.exe') 'CURRENCY_SAMPLE_RESULT'
    Invoke-Host 'avalonia' (Join-Path $app 'Currency.Avalonia.Windows.exe') 'CURRENCY_AVALONIA_RESULT'
    Invoke-Python eng/verify-shared-hosting.py results @proof
    Write-Output "RUNTIME PASS: both Windows Currency hosts executed; evidence at $runRoot"
}
finally { Pop-Location }
