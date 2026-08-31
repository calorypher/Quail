[CmdletBinding()]
param(
    [string] $IsccPath,
    [string] $PrerequisiteManifestPath,
    [string] $PrerequisitePinsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Version([string] $Root) {
    [xml] $properties = Get-Content -Raw -LiteralPath (Join-Path $Root 'Directory.Build.props')
    $version = $properties.SelectSingleNode('/Project/PropertyGroup/Version')
    if ($null -eq $version -or [string]::IsNullOrWhiteSpace($version.InnerText)) { throw 'Directory.Build.props does not define one canonical Version.' }
    return $version.InnerText.Trim()
}

function Resolve-Iscc([string] $ExplicitPath) {
    $candidates = @()
    if ($ExplicitPath) { $candidates += $ExplicitPath }
    else {
        $onPath = Get-Command 'ISCC.exe' -CommandType Application -ErrorAction SilentlyContinue
        if ($onPath) { $candidates += $onPath.Source }
        $candidates += 'C:\Program Files (x86)\Inno Setup 7\ISCC.exe', 'C:\Program Files\Inno Setup 7\ISCC.exe', 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe', 'C:\Program Files\Inno Setup 6\ISCC.exe'
        if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe') }
    }
    $valid = @($candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) -and ([IO.Path]::GetFileName($_) -ieq 'ISCC.exe') } | ForEach-Object { (Resolve-Path -LiteralPath $_).Path } | Sort-Object -Unique)
    if ($valid.Count -ne 1) { throw "Expected one usable ISCC.exe; found $($valid.Count). Supply -IsccPath explicitly." }
    return $valid[0]
}

function Get-Prerequisite($Prerequisites, [string] $Id) {
    $entry = @($Prerequisites | Where-Object { $_.id -eq $Id })
    if ($entry.Count -ne 1 -or -not (Test-Path -LiteralPath $entry[0].resolvedPath -PathType Leaf)) { throw "Missing usable prerequisite cache entry: $Id." }
    return $entry[0]
}

function Get-Pin($Pins, [string] $Id) {
    $entry = @($Pins | Where-Object { $_.id -eq $Id })
    if ($entry.Count -ne 1) { throw "Canonical prerequisite pins must contain exactly one $Id entry." }
    return $entry[0]
}

function Assert-PrerequisiteMatchesPin([object] $Prerequisite, [object] $Pin) {
    foreach ($property in @('id', 'fileName', 'source', 'sha256')) {
        if ([string]$Prerequisite.$property -ne [string]$Pin.$property) { throw "Prerequisite cache entry $($Prerequisite.id) does not match canonical $property pin." }
    }

    foreach ($property in @('requiredVersion', 'minimumVersion', 'channel')) {
        $pinProperty = $Pin.PSObject.Properties[$property]
        $cacheProperty = $Prerequisite.PSObject.Properties[$property]
        if (($null -eq $pinProperty) -ne ($null -eq $cacheProperty) -or
            ($null -ne $pinProperty -and [string]$cacheProperty.Value -ne [string]$pinProperty.Value)) {
            throw "Prerequisite cache entry $($Prerequisite.id) does not match canonical $property metadata."
        }
    }

    if ((Get-FileHash -LiteralPath $Prerequisite.resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $Pin.sha256.ToLowerInvariant()) {
        throw "Prerequisite cache file does not match canonical SHA-256 pin: $($Prerequisite.resolvedPath)"
    }
}

function Copy-Payload([string] $Source, [string] $Destination) {
    Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($Source.Length).TrimStart('\')
        $target = Join-Path $Destination $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            if ((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash) { throw "Publish collision has different content: $relative" }
        }
        else { Copy-Item -LiteralPath $_.FullName -Destination $target }
    }
}

function Copy-RequiredAppArtifact([string] $BuildOutput, [string] $PublishOutput, [string] $Artifact) {
    $source = Join-Path $BuildOutput $Artifact
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Release App build is missing required unpackaged WinUI artifact: $source" }
    $destination = Join-Path $PublishOutput $Artifact
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$root = Split-Path -Parent $PSScriptRoot
$pinsPath = if ($PrerequisitePinsPath) { $PrerequisitePinsPath } else { Join-Path $root 'packaging\prerequisite-pins.json' }
$pinsPath = (Resolve-Path -LiteralPath $pinsPath -ErrorAction Stop).Path
$version = Get-Version $root
$stage = Join-Path $root 'artifacts\installer-staging\win-x64'
$appPublish = Join-Path $stage 'app'
$cliPublish = Join-Path $stage 'cli'
$payload = Join-Path $stage 'payload'
$installerDirectory = Join-Path $root "artifacts\installer\\$version"
$installer = Join-Path $installerDirectory "Quail-$version-Setup.exe"
$iscc = Resolve-Iscc $IsccPath

foreach ($directory in @($stage, $installerDirectory)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

foreach ($publish in @(
    @{ Project = 'src\Quail.App\Quail.App.csproj'; Output = $appPublish },
    @{ Project = 'src\Quail.Cli\Quail.Cli.csproj'; Output = $cliPublish })) {
    & dotnet restore (Join-Path $root $publish.Project) --runtime win-x64 --ignore-failed-sources -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed for $($publish.Project) with exit code $LASTEXITCODE." }
    & dotnet publish (Join-Path $root $publish.Project) --configuration Release --runtime win-x64 --self-contained false --output $publish.Output --no-restore -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:PublishAot=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $($publish.Project) with exit code $LASTEXITCODE." }
}

$appBuildOutput = Join-Path $root 'src\Quail.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'
foreach ($artifact in @('App.xbf', 'QuickSearchWindow.xbf', 'Quail.pri', 'Assets\quail-feather-A-gradient.svg', 'Assets\quail-app-icon-32px.png', 'Assets\quail-app-icon-48px.png', 'Assets\quail-tray-icon-16px.png')) { Copy-RequiredAppArtifact $appBuildOutput $appPublish $artifact }

New-Item -ItemType Directory -Path $payload -Force | Out-Null
Copy-Payload $appPublish $payload
Copy-Payload $cliPublish $payload

$required = @('Quail.exe', 'Quail.Cli.exe', 'Quail.FileSystem.dll', 'App.xbf', 'QuickSearchWindow.xbf', 'Quail.pri', 'Assets\quail-feather-A-gradient.svg', 'Assets\quail-app-icon-32px.png', 'Assets\quail-app-icon-48px.png', 'Assets\quail-tray-icon-16px.png', 'Microsoft.Data.Sqlite.dll', 'SQLitePCLRaw.batteries_v2.dll', 'SQLitePCLRaw.core.dll', 'SQLitePCLRaw.provider.e_sqlite3.dll', 'e_sqlite3.dll')
foreach ($artifact in $required) { if (-not (Test-Path -LiteralPath (Join-Path $payload $artifact) -PathType Leaf)) { throw "Final installer payload is missing required artifact: $artifact" } }
foreach ($runtimeFile in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll')) { if (Test-Path -LiteralPath (Join-Path $payload $runtimeFile) -PathType Leaf) { throw "Framework-dependent payload unexpectedly contains private .NET runtime file: $runtimeFile" } }
foreach ($unusedAiMlFile in @('DirectML.dll', 'onnxruntime.dll', 'Microsoft.ML.OnnxRuntime.dll', 'Microsoft.Windows.AI.MachineLearning.dll', 'Microsoft.Windows.AI.MachineLearning.Projection.dll', 'System.Numerics.Tensors.dll')) {
    if (Test-Path -LiteralPath (Join-Path $payload $unusedAiMlFile) -PathType Leaf) { throw "Framework-dependent payload unexpectedly contains unused AI/ML file: $unusedAiMlFile" }
}
if (Get-ChildItem -LiteralPath $payload -Filter 'Microsoft.Windows.AI.*.Projection.dll' -File) { throw 'Framework-dependent payload unexpectedly contains unused Windows AI projection files.' }

& (Join-Path $PSScriptRoot 'test-release-build-provenance.ps1') -PayloadDirectory $payload -PhysicalCheckoutRoot $root
if ($LASTEXITCODE -ne 0) { throw 'Release build provenance guard failed.' }

if (-not $PrerequisiteManifestPath) {
    & (Join-Path $PSScriptRoot 'get-installer-prerequisites.ps1') -PinsPath $pinsPath
    if ($LASTEXITCODE -ne 0) { throw 'Prerequisite acquisition failed.' }
    $PrerequisiteManifestPath = Join-Path $root '.quail-tooling\installer-prerequisites\prerequisites.json'
}
if (-not (Test-Path -LiteralPath $PrerequisiteManifestPath -PathType Leaf)) { throw "Prerequisite manifest does not exist: $PrerequisiteManifestPath" }
$pinDocument = Get-Content -Raw -LiteralPath $pinsPath | ConvertFrom-Json
if ($pinDocument.schemaVersion -ne 1 -or $null -eq $pinDocument.prerequisites) { throw 'Unsupported canonical prerequisite pin manifest.' }
$prerequisites = Get-Content -Raw -LiteralPath $PrerequisiteManifestPath | ConvertFrom-Json
$dotnet = Get-Prerequisite $prerequisites 'dotnet-desktop-runtime-x64'
$windowsAppRuntime = Get-Prerequisite $prerequisites 'windows-app-runtime-x64'
$vcRedist = Get-Prerequisite $prerequisites 'vc-redist-x64'
foreach ($pair in @(
    @{ Prerequisite = $dotnet; Pin = (Get-Pin $pinDocument.prerequisites 'dotnet-desktop-runtime-x64') },
    @{ Prerequisite = $windowsAppRuntime; Pin = (Get-Pin $pinDocument.prerequisites 'windows-app-runtime-x64') },
    @{ Prerequisite = $vcRedist; Pin = (Get-Pin $pinDocument.prerequisites 'vc-redist-x64') })) {
    Assert-PrerequisiteMatchesPin $pair.Prerequisite $pair.Pin
}
if (-not $dotnet.requiredVersion -or -not $windowsAppRuntime.requiredVersion -or -not $vcRedist.minimumVersion) { throw 'Prerequisite manifest is missing detector version metadata.' }
$runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $payload 'Quail.runtimeconfig.json') | ConvertFrom-Json
$framework = $runtimeConfig.runtimeOptions.framework
if ($framework.name -ne 'Microsoft.NETCore.App') { throw "Unexpected App runtimeconfig framework: $($framework.name)" }
$frameworkVersion = [version]$framework.version
$rollForward = if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'rollForward') { [string]$runtimeConfig.runtimeOptions.rollForward } else { 'default' }
$allowsMajorRollForward = $rollForward -in @('Major', 'LatestMajor')
$windowsAppRuntimeDetector = Get-Content -Raw -LiteralPath (Join-Path $root 'packaging\WindowsAppRuntimeDetection.psm1')
$windowsAppRuntimeCheck = $windowsAppRuntimeDetector + "`r`nif (Test-QuailWindowsAppRuntime -Packages @(Get-AppxPackage) -MinimumVersion ([version]'$($windowsAppRuntime.requiredVersion)')) { exit 0 }`r`nexit 1`r`n"
$windowsAppRuntimeCheckBase64 = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($windowsAppRuntimeCheck))

& $iscc "/DAppVersion=$version" "/DSourceDir=$payload" "/DOutputDir=$installerDirectory" "/DDotNetDesktopUrl=$($dotnet.source)" "/DDotNetDesktopSha256=$($dotnet.sha256)" "/DDotNetDesktopMinimumVersion=$($dotnet.requiredVersion)" "/DDotNetRuntimeMajor=$($frameworkVersion.Major)" "/DDotNetAllowsMajorRollForward=$([int]$allowsMajorRollForward)" "/DWindowsAppRuntimeUrl=$($windowsAppRuntime.source)" "/DWindowsAppRuntimeSha256=$($windowsAppRuntime.sha256)" "/DWindowsAppRuntimeMinimumVersion=$($windowsAppRuntime.requiredVersion)" "/DWindowsAppRuntimeCheckBase64=$windowsAppRuntimeCheckBase64" "/DVcRedistUrl=$($vcRedist.source)" "/DVcRedistSha256=$($vcRedist.sha256)" "/DVcRedistMinimumVersion=$($vcRedist.minimumVersion)" (Join-Path $root 'packaging\Quail.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Expected installer was not produced: $installer" }

$files = @(Get-ChildItem -LiteralPath $payload -Recurse -File)
$summary = [ordered]@{ sourceCommit = (& git -C $root rev-parse HEAD).Trim(); version = $version; deployment = 'framework-dependent unpackaged WinUI 3'; payloadPath = $payload; payloadFileCount = $files.Count; payloadBytes = ($files | Measure-Object -Property Length -Sum).Sum; prerequisites = $prerequisites; installerPath = $installer; installerBytes = (Get-Item -LiteralPath $installer).Length; installerSha256 = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() }
$summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding utf8
Write-Output "PASS installer=$installer version=$version payload=$payload fileCount=$($summary.payloadFileCount) payloadBytes=$($summary.payloadBytes) installerBytes=$($summary.installerBytes) sha256=$($summary.installerSha256) iscc=$iscc"
