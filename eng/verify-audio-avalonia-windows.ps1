param(
    [Parameter(Mandatory = $true)]
    [string]$AppDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$app = (Resolve-Path -LiteralPath $AppDirectory).Path
$executable = Join-Path $app 'AudioStreamer.Windows.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw "Missing executable: $executable" }
$runRoot = Join-Path $repoRoot ('artifacts/audio-avalonia/windows/run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$destination = Join-Path $runRoot 'result.txt'
$stdout = Join-Path $runRoot 'stdout.log'
$stderr = Join-Path $runRoot 'stderr.log'
$previous = $env:AUDIO_AVALONIA_RESULT
$process = $null
try {
    $env:AUDIO_AVALONIA_RESULT = $destination
    $process = Start-Process -FilePath $executable -ArgumentList '--verify' -WorkingDirectory $app `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(45000)) { throw 'Audio verification or window cleanup timed out.' }
    $process.Refresh()
    if (-not (Test-Path -LiteralPath $destination)) { throw "No result file; see $runRoot" }
    $result = [IO.File]::ReadAllText($destination)
    $log = [IO.File]::ReadAllText($stdout)
    $closed = [regex]::Matches($log, '(?m)^AUDIO_AVALONIA_CLOSED\r?$').Count
    if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS ') -or $closed -ne 1 -or
        $log.Contains('AUDIO_AVALONIA FAIL') -or
        -not $result.Contains('host recreation, controls and minimize/restart')) {
        throw "Audio verification failed (exit $($process.ExitCode), close markers $closed): $result; see $runRoot"
    }
    $files = @(Get-ChildItem -LiteralPath $app -Recurse -File | Sort-Object FullName | ForEach-Object {
        @{ path = [IO.Path]::GetRelativePath($app, $_.FullName); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    @{
        timestamp = [DateTime]::UtcNow.ToString('o')
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        exitCode = $process.ExitCode
        closeMarkers = $closed
        result = $result
        application = $files
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'verification.json') -Encoding utf8
    Write-Output "AUDIO_AVALONIA $result"
    Write-Output "Evidence: $runRoot"
}
finally {
    $env:AUDIO_AVALONIA_RESULT = $previous
    if ($process -and -not $process.HasExited) {
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(5000)) { Stop-Process -Id $process.Id }
    }
}
