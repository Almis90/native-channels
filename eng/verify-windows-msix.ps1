param(
    [Parameter(Mandatory)][string]$PackagePath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string[]]$DependencyPath = @(),
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
$PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
$DependencyPath = @($DependencyPath | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })
if ((Get-Process -Id $PID).SessionId -eq 0) { throw 'MSIX verification requires an interactive desktop session.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (-not $entry -or -not $archive.GetEntry('AppxSignature.p7x')) { throw 'A signed MSIX package is required.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
}
finally { $archive.Dispose() }
$identity = [string]$manifest.Package.Identity.Name
switch ($identity) {
    'dev.nativechannels.sample' { $resultName = 'sample-result.txt'; $prefix = 'PASS Windows:' }
    'dev.nativechannels.currency.sample' { $resultName = 'currency-result.txt'; $prefix = 'PASS windows:' }
    'dev.nativechannels.audio.sample' { $resultName = 'audio-result.txt'; $prefix = 'PASS ' }
    'dev.nativechannels.volume.sample' { $resultName = 'volume-result.txt'; $prefix = 'PASS ' }
    'dev.nativechannels.pdf.sample' { $resultName = 'pdf-result.txt'; $prefix = 'PASS ' }
    default { throw "This verifier only installs the NativeChannels verification samples, not $identity." }
}
if (Get-AppxPackage -Name $identity) { throw "Already installed: $identity. Use a clean test account to avoid replacing its app or data." }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
if (Test-Path -LiteralPath (Join-Path $OutputDirectory 'verification.json')) { throw 'Use a new output directory for each verification.' }

if (-not ('NativeChannels.MsixVerification' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
namespace NativeChannels {
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IApplicationActivationManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
        [PreserveSig] int ActivateForFile(IntPtr appUserModelId, IntPtr items, IntPtr verb, out uint processId);
        [PreserveSig] int ActivateForProtocol(IntPtr appUserModelId, IntPtr items, out uint processId);
    }
    public static class MsixVerification {
        public static uint Activate(string appUserModelId) {
            var manager = (IApplicationActivationManager)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
            try {
                uint processId;
                Marshal.ThrowExceptionForHR(manager.ActivateApplication(appUserModelId, null, 0, out processId));
                return processId;
            } finally { Marshal.ReleaseComObject(manager); }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder name);
        public static string PackageName(IntPtr process) {
            uint length = 0;
            int status = GetPackageFullName(process, ref length, null);
            if (status != 122) throw new System.ComponentModel.Win32Exception(status);
            var name = new StringBuilder((int)length);
            status = GetPackageFullName(process, ref length, name);
            if (status != 0) throw new System.ComponentModel.Win32Exception(status);
            return name.ToString();
        }
    }
}
'@
}

$record = [ordered]@{
    mode = 'runtime'; deployment = 'msix'
    os = [Environment]::OSVersion.VersionString
    ubr = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').UBR
    sessionId = (Get-Process -Id $PID).SessionId
    packageSha256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    packageName = $identity
    architecture = [string]$manifest.Package.Identity.ProcessorArchitecture
    dependencyHashes = @($DependencyPath | ForEach-Object {
        [ordered]@{ file = [IO.Path]::GetFileName($_); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    startedUtc = [DateTime]::UtcNow.ToString('o')
}
$installed = $null
$process = $null
try {
    # The signing certificate must already be trusted by the test machine.
    if ($DependencyPath.Count) { Add-AppxPackage -Path $PackagePath -DependencyPath $DependencyPath }
    else { Add-AppxPackage -Path $PackagePath }
    $installed = Get-AppxPackage -Name $identity
    if (-not $installed -or $installed.Status -ne 'Ok') { throw 'The installed package is not healthy.' }
    $record.packageFullName = [string]$installed.PackageFullName
    $record.dependencies = @($installed.Dependencies | ForEach-Object { [string]$_.PackageFullName })
    $result = Join-Path $env:LOCALAPPDATA "Packages/$($installed.PackageFamilyName)/LocalState/$resultName"
    if (Test-Path -LiteralPath $result) { throw 'An old result exists; use a clean test account.' }
    $appId = [string]$manifest.Package.Applications.Application.Id
    $record.appUserModelId = "$($installed.PackageFamilyName)!$appId"
    $activatedProcessId = [NativeChannels.MsixVerification]::Activate($record.appUserModelId)
    $process = Get-Process -Id $activatedProcessId
    $record.processPackageFullName = [NativeChannels.MsixVerification]::PackageName($process.Handle)
    if ($record.processPackageFullName -ne $record.packageFullName) { throw 'The activated process has the wrong package identity.' }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $report = ''
    while (-not $report) {
        if (Test-Path -LiteralPath $result) {
            try { $report = [IO.File]::ReadAllText($result) } catch [IO.IOException] { }
            if ($report) { break }
        }
        if ($process.HasExited) { throw "The packaged sample exited before writing a result (exit $($process.ExitCode))." }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'MSIX sample verification timed out.' }
        Start-Sleep -Milliseconds 500
    }
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'result.txt'), $report)
    if (-not $report.StartsWith($prefix)) { throw "Packaged sample failed: $report" }
    $record.report = $report
    Write-Output $report
}
catch {
    $record.error = $_.Exception.Message
    throw
}
finally {
    try {
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
        if ($installed) {
            Remove-AppxPackage -Package $installed.PackageFullName
            $record.uninstalled = -not [bool](Get-AppxPackage -Name $identity)
            if (-not $record.uninstalled) { throw 'The test package was not removed.' }
        }
    }
    finally {
        $record.completedUtc = [DateTime]::UtcNow.ToString('o')
        $record | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Encoding utf8
    }
}
