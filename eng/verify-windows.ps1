param(
    [string]$Version = '0.1.0-preview.2',
    [string]$CurrencyVersion = '1.1.0-alpha.1',
    [string]$PackageFeed,
    [switch]$WindowsOnlyFeed,
    [string]$PackageCache,
    [string]$VerificationDirectory,
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$RuntimeIdentifier = 'win-x64',
    [switch]$Currency,
    [ValidateSet('Currency', 'AudioStreamer', 'VolumeMonitor', 'PdfViewer')]
    [string]$Plugin,
    [switch]$PackOnly,
    [switch]$BuildOnly,
    [switch]$Msix,
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
if ($Currency -and $Plugin -and $Plugin -ne 'Currency') { throw 'Currency and Plugin select different samples.' }
if ($Currency) { $Plugin = 'Currency' }
if ($WindowsOnlyFeed -and -not $PackageFeed) { throw 'WindowsOnlyFeed requires PackageFeed.' }
$repoRoot = Split-Path $PSScriptRoot -Parent
$localDotnet = Join-Path $repoRoot '.tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path $localDotnet) { $localDotnet } else { 'dotnet' }
$framework = 'net10.0-windows10.0.19041.0'
$msixHash = $null
$msixDependencyHashes = @()
$packages = if ($PackageFeed) { (Resolve-Path -LiteralPath $PackageFeed).Path } else { Join-Path $repoRoot 'artifacts/windows/packages' }
if ($PackOnly -and $PackageFeed) { throw 'PackOnly cannot be used with an existing package feed.' }
if ($Msix -and ($PackOnly -or -not $CertificateThumbprint)) { throw 'Msix requires CertificateThumbprint and cannot be used with PackOnly.' }
if ($CertificateThumbprint -and -not $Msix) { throw 'CertificateThumbprint requires Msix.' }
if (-not ($BuildOnly -or $PackOnly) -and $RuntimeIdentifier -ne "win-$([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant())") {
    throw 'Runtime verification must use the native OS architecture; use BuildOnly for cross-compilation.'
}
$runRoot = Join-Path $repoRoot ('artifacts/windows/verification/' + [Guid]::NewGuid().ToString('N'))
if ($VerificationDirectory) {
    $runRoot = [IO.Path]::GetFullPath($VerificationDirectory, $repoRoot)
    if (Test-Path -LiteralPath $runRoot) { throw 'VerificationDirectory must be a new directory.' }
}
# The WinUI XAML compiler still hits MAX_PATH for long projection assembly names.
$cache = if ($PackageCache) { [IO.Path]::GetFullPath($PackageCache, $repoRoot) } else { Join-Path $repoRoot ('.tools/wn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12)) }
$output = Join-Path $runRoot 'app'
New-Item -ItemType Directory -Force $packages, $runRoot | Out-Null

function Invoke-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}

function Test-WindowsPackage([string]$Project, [string]$PackageVersion) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $packages "$Project.$PackageVersion.nupkg"))
    try {
        if (-not $archive.GetEntry("lib/net10.0-windows10.0.19041/$Project.dll")) { throw "Missing Windows assembly in $Project" }
        $reader = [IO.StreamReader]::new($archive.GetEntry("$Project.nuspec").Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $groups = @($manifest.package.metadata.dependencies.group | Where-Object { $_.targetFramework -like '*windows*' })
        if ($groups.Count -ne 1) { throw "Missing Windows dependency group in $Project" }
        if ($groups.dependency.id -match 'NativeChannels.Native.(Apple|Android)') { throw "Mobile binding leaked into $Project" }
        if ((-not $PackageFeed -or $WindowsOnlyFeed) -and $archive.Entries.FullName -match '^native/') { throw "Mobile native assets leaked into Windows-only $Project" }
        if ($PackageFeed -and -not $WindowsOnlyFeed) {
            foreach ($platform in @('android', 'ios', 'maccatalyst')) {
                if (-not ($archive.Entries.FullName -like "lib/net10.0-$platform*/$Project.dll")) { throw "Missing $platform assembly in combined $Project" }
            }
        }
    }
    finally { $archive.Dispose() }
}

function Write-VerificationRecord([string]$Mode, [string]$Report) {
    $hashes = [ordered]@{}
    foreach ($project in $packageVersions.Keys) {
        $file = "$project.$($packageVersions[$project]).nupkg"
        $hashes[$file] = (Get-FileHash (Join-Path $packages $file) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    [ordered]@{
        mode = $Mode
        os = [Runtime.InteropServices.RuntimeInformation]::OSDescription
        hostArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        runtimeIdentifier = $RuntimeIdentifier
        sample = $sample
        combinedPackages = [bool]($PackageFeed -and -not $WindowsOnlyFeed)
        deployment = $(if ($Msix) { 'msix' } else { 'unpackaged' })
        msixSha256 = $msixHash
        msixDependencyHashes = $msixDependencyHashes
        packageHashes = $hashes
        report = $Report
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $runRoot 'verification.json') -Encoding utf8
}

Push-Location $repoRoot
try {
    if (-not $PackageFeed) {
        Invoke-Dotnet test -c Release '-p:RestoreSources=https://api.nuget.org/v3/index.json'
        foreach ($project in @('NativeChannels.Core', 'NativeChannels', 'NativeChannels.Maui')) {
            Invoke-Dotnet pack "src/$project" -c Release "-p:Version=$Version" '-p:NativeChannelsWindowsOnly=true' `
                '-p:RestoreSources=https://api.nuget.org/v3/index.json' -o $packages
        }
    }

    # Check the actual dependency group; mobile native bindings must never leak into Windows.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($project in @('NativeChannels', 'NativeChannels.Maui')) {
        Test-WindowsPackage $project $Version
    }

    $config = Join-Path $runRoot 'NuGet.Config'
    $escapedPackages = [Security.SecurityElement]::Escape($packages)
    @"
<configuration><packageSources><clear />
<add key="local" value="$escapedPackages" />
<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
</packageSources><packageSourceMapping>
<packageSource key="local"><package pattern="NativeChannels*" /><package pattern="Plugin.Maui.*" /></packageSource>
<packageSource key="nuget.org"><package pattern="*" /></packageSource>
</packageSourceMapping></configuration>
"@ | Set-Content -LiteralPath $config -Encoding utf8
    $sample = 'samples/Maui/ChannelExplorer/ChannelExplorer.csproj'
    $executable = 'ChannelExplorer.exe'
    $resultName = 'sample-result.txt'
    $resultVariable = 'NATIVECHANNELS_SAMPLE_RESULT'
    $passPrefix = 'PASS Windows:'
    $packageVersions = [ordered]@{ 'NativeChannels.Core' = $Version; 'NativeChannels' = $Version; 'NativeChannels.Maui' = $Version }
    if ($Plugin) {
        $pluginProject = "plugins/Plugin.Maui.$Plugin/src/Plugin.Maui.$Plugin/Plugin.Maui.$Plugin.csproj"
        $pluginVersion = if ($Plugin -eq 'Currency') { $CurrencyVersion } else { '0.1.0-alpha.1' }
        if (-not $PackageFeed) {
            $pluginCache = Join-Path $repoRoot ('.tools/wn/' + [Guid]::NewGuid().ToString('N').Substring(0, 12))
            $pluginSettings = @('-p:NativeChannelsWindowsOnly=true', "-p:NativeChannelsPackageVersion=$Version", "-p:Version=$pluginVersion", "-p:RestorePackagesPath=$pluginCache")
            Invoke-Dotnet restore $pluginProject @pluginSettings --configfile $config
            Invoke-Dotnet pack $pluginProject @pluginSettings -c Release --no-restore -o $packages
        }
        Test-WindowsPackage "Plugin.Maui.$Plugin" $pluginVersion
        $packageVersions["Plugin.Maui.$Plugin"] = $pluginVersion
        $sample = "samples/Maui/$Plugin/$Plugin.Sample.csproj"
        $executable = "$Plugin.Sample.exe"
        $shortName = @{ Currency = 'currency'; AudioStreamer = 'audio'; VolumeMonitor = 'volume'; PdfViewer = 'pdf' }[$Plugin]
        $resultName = "$shortName-result.txt"
        $resultVariable = "$($shortName.ToUpperInvariant())_SAMPLE_RESULT"
        $passPrefix = if ($Plugin -eq 'Currency') { 'PASS windows:' } else { 'PASS ' }
    }
    if ($PackOnly) {
        Write-Output "PACK ONLY: Windows platform inputs at $packages"
        return
    }
    if ($Plugin) {
        $tests = if ($Plugin -eq 'Currency') { 'tests/Plugin.Maui.Currency.Windows.Tests/Plugin.Maui.Currency.Windows.Tests.csproj' } else { 'tests/Plugin.Maui.Windows.Tests/Plugin.Maui.Windows.Tests.csproj' }
        $testSettings = @("-p:CurrencyPackageVersion=$CurrencyVersion", "-p:RestorePackagesPath=$cache", "-p:WindowsTestPlugin=$Plugin")
        Invoke-Dotnet restore $tests @testSettings --configfile $config
        # Hosted CI has no guaranteed audio endpoint. Runtime mode runs every hardware test,
        # including a temporary volume change which the test restores in finally.
        $testArguments = if ($BuildOnly) { @('--filter', 'Category!=AudioHardware') } else { @() }
        Invoke-Dotnet test $tests @testSettings @testArguments -c Release --no-restore --logger trx --results-directory $runRoot
    }
    $settings = @('-p:NativeChannelsWindowsOnly=true', "-p:NativeChannelsPackageVersion=$Version", "-p:CurrencyPackageVersion=$CurrencyVersion", "-p:RestorePackagesPath=$cache", "-p:RuntimeIdentifier=$RuntimeIdentifier", '-p:SelfContained=true', '-p:Configuration=Release')
    if ($Msix) {
        $settings += @('-p:WindowsPackageType=MSIX', '-p:GenerateAppxPackageOnBuild=true',
            '-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateThumbprint=$CertificateThumbprint",
            '-p:AppxBundle=Never', "-p:AppxPackageDir=$runRoot/msix/")
    }
    Invoke-Dotnet restore $sample @settings --configfile $config
    foreach ($project in $packageVersions.Keys) {
        $id = $project.ToLowerInvariant()
        $packageVersion = $packageVersions[$project]
        $cached = Join-Path $cache "$id/$packageVersion/$id.$packageVersion.nupkg"
        $source = Join-Path $packages "$project.$packageVersion.nupkg"
        if ((Get-FileHash $cached).Hash -ne (Get-FileHash $source).Hash) { throw "Consumer restored a different $project package" }
    }
    $assets = Get-Content (Join-Path (Split-Path $sample) 'obj/project.assets.json') -Raw | ConvertFrom-Json
    if ($assets.libraries.PSObject.Properties.Value.type -contains 'project') { throw 'Project reference entered the package consumer' }
    if ($assets.libraries.PSObject.Properties.Name -match '^NativeChannels.Native\.(Apple|Android)/') { throw 'Mobile binding entered the Windows consumer' }
    Invoke-Dotnet publish $sample @settings -f $framework -c Release --no-restore -o $output
    if ($Msix) {
        $installers = @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'msix') -Recurse -Filter '*.msix' |
            Where-Object { $_.FullName -notmatch '[\\/]Dependencies[\\/]' })
        if ($installers.Count -ne 1) { throw 'Expected exactly one MSIX installer.' }
        $archive = [IO.Compression.ZipFile]::OpenRead($installers[0].FullName)
        try {
            if (-not $archive.GetEntry('AppxSignature.p7x')) { throw 'The MSIX is unsigned.' }
            $reader = [IO.StreamReader]::new($archive.GetEntry('AppxManifest.xml').Open())
            try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($manifest.Package.Dependencies.TargetDeviceFamily.MinVersion -ne '10.0.17763.0') { throw 'The MSIX minimum OS does not match the supported Windows version.' }
            if ($manifest.Package.Identity.ProcessorArchitecture -ne $RuntimeIdentifier.Substring(4)) { throw 'The MSIX architecture does not match the requested RID.' }
            if (-not ($manifest.Package.Dependencies.PackageDependency.Name -like 'Microsoft.WindowsAppRuntime.*')) { throw 'MSIX needs a Windows App Runtime dependency for Windows 10 1809.' }
        }
        finally { $archive.Dispose() }
        $msixHash = (Get-FileHash -LiteralPath $installers[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $dependencyDirectory = Join-Path $installers[0].Directory.FullName "Dependencies/$($RuntimeIdentifier.Substring(4))"
        $dependencies = @(Get-ChildItem -LiteralPath $dependencyDirectory -File | Where-Object Extension -in @('.msix', '.appx'))
        if (-not $dependencies.Count) { throw 'The MSIX dependency directory is empty.' }
        $msixDependencyHashes = @($dependencies | ForEach-Object {
            [ordered]@{ file = $_.Name; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
        })
        if (-not $BuildOnly) {
            & (Join-Path $PSScriptRoot 'verify-windows-msix.ps1') -PackagePath $installers[0].FullName -DependencyPath $dependencies.FullName -OutputDirectory (Join-Path $runRoot 'installed')
            Write-VerificationRecord 'runtime' ([IO.File]::ReadAllText((Join-Path $runRoot 'installed/result.txt')))
        }
        else { Write-VerificationRecord 'compile' '' }
        Write-Output "MSIX: $($installers[0].FullName)"
        return
    }
    if ($BuildOnly) {
        Write-VerificationRecord 'compile' ''
        Write-Output "COMPILE ONLY: Windows package consumer at $output"
        return
    }

    $result = Join-Path $runRoot $resultName
    $previousResult = [Environment]::GetEnvironmentVariable($resultVariable)
    $process = $null
    try {
        [Environment]::SetEnvironmentVariable($resultVariable, $result)
        $process = Start-Process -FilePath (Join-Path $output $executable) -PassThru -WindowStyle Hidden
        $deadline = [DateTime]::UtcNow.AddSeconds(120)
        $report = ''
        while (-not $report) {
            if (Test-Path -LiteralPath $result) {
                try { $report = [IO.File]::ReadAllText($result) } catch [IO.IOException] { }
                if ($report) { break }
            }
            if ($process.HasExited) { throw "Windows sample exited before writing a result (exit $($process.ExitCode))." }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Windows sample verification timed out.' }
            Start-Sleep -Milliseconds 500
        }
        Write-Output $report
        if (-not $report.StartsWith($passPrefix)) { throw "Windows sample failed. See $result" }
        Write-VerificationRecord 'runtime' $report
    }
    finally {
        [Environment]::SetEnvironmentVariable($resultVariable, $previousResult)
        if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id }
    }
}
finally { Pop-Location }
