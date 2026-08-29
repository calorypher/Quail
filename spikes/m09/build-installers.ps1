[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $FixtureVersion = '0.9.0',
    [string] $IsccPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-Iscc([string] $ExplicitPath) {
    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "-IsccPath does not name a file: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @(@(
        (Get-Command ISCC.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
        'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
        'C:\Program Files\Inno Setup 7\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } |
        Sort-Object -Unique)

    if ($candidates.Count -ne 1) {
        throw "Expected exactly one Inno Setup compiler; found $($candidates.Count). Supply -IsccPath explicitly."
    }

    return (Resolve-Path -LiteralPath $candidates[0]).Path
}

function Get-Prerequisite($Prerequisites, [string] $Id) {
    $entry = @($Prerequisites | Where-Object { $_.id -eq $Id })
    if ($entry.Count -ne 1 -or -not (Test-Path -LiteralPath $entry[0].resolvedPath -PathType Leaf)) {
        throw "Missing usable prerequisite cache entry: $Id. Run get-prerequisites.ps1 first."
    }
    return $entry[0]
}

function Get-RuntimeRequirement([string] $RuntimeConfigPath) {
    if (-not (Test-Path -LiteralPath $RuntimeConfigPath -PathType Leaf)) {
        throw "Framework-dependent runtimeconfig is missing: $RuntimeConfigPath"
    }

    $runtimeConfig = Get-Content -Raw -LiteralPath $RuntimeConfigPath | ConvertFrom-Json
    $framework = $runtimeConfig.runtimeOptions.framework
    if ($framework.name -ne 'Microsoft.NETCore.App') {
        throw "Unexpected runtimeconfig framework: $($framework.name)"
    }

    $frameworkVersion = [version]$framework.version
    $rollForward = if ($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'rollForward') {
        [string]$runtimeConfig.runtimeOptions.rollForward
    }
    else {
        'default'
    }

    [pscustomobject]@{
        FrameworkMajor = $frameworkVersion.Major
        AllowsMajorRollForward = $rollForward -in @('Major', 'LatestMajor')
        RollForward = $rollForward
    }
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installerDirectory = Join-Path $repositoryRoot 'spikes\m09\installers'
$outputDirectory = Join-Path $repositoryRoot "artifacts\m09\installer\$FixtureVersion"
$manifestPath = Join-Path $repositoryRoot '.quail-tooling\m09-prerequisites\prerequisites.json'
$iscc = Resolve-Iscc $IsccPath

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& (Join-Path $PSScriptRoot 'publish-m09.ps1') -Variant self-contained -FixtureVersion $FixtureVersion
if ($LASTEXITCODE -ne 0) { throw 'Self-contained publish failed.' }
& (Join-Path $PSScriptRoot 'publish-m09.ps1') -Variant framework-dependent -FixtureVersion $FixtureVersion
if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent publish failed.' }

$selfContainedPublish = Join-Path $repositoryRoot "artifacts\m09\publish\self-contained-$FixtureVersion"
$frameworkDependentPublish = Join-Path $repositoryRoot "artifacts\m09\publish\framework-dependent-$FixtureVersion"

& $iscc "/DAppVersion=$FixtureVersion" "/DSourceDir=$selfContainedPublish" "/DOutputDir=$outputDirectory" (Join-Path $installerDirectory 'Quail.M09.SelfContained.iss')
if ($LASTEXITCODE -ne 0) { throw 'Self-contained Inno Setup compilation failed.' }

if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Prerequisite manifest is missing: $manifestPath. Run get-prerequisites.ps1 first."
}

$prerequisites = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$dotnet = Get-Prerequisite $prerequisites 'dotnet-desktop-runtime-x64'
$windowsAppRuntime = Get-Prerequisite $prerequisites 'windows-app-runtime-x64'
$vcRedist = Get-Prerequisite $prerequisites 'vc-redist-x64'
$runtimeRequirement = Get-RuntimeRequirement (Join-Path $frameworkDependentPublish 'Quail.M08.WinUi.runtimeconfig.json')

if (-not $dotnet.requiredVersion -or -not $windowsAppRuntime.requiredVersion -or -not $vcRedist.minimumVersion) {
    throw 'Prerequisite manifest is missing the version metadata required for installer detection.'
}

& $iscc "/DAppVersion=$FixtureVersion" "/DSourceDir=$frameworkDependentPublish" "/DOutputDir=$outputDirectory" "/DDotNetDesktopUrl=$($dotnet.source)" "/DDotNetDesktopSha256=$($dotnet.sha256)" "/DDotNetDesktopMinimumVersion=$($dotnet.requiredVersion)" "/DDotNetRuntimeMajor=$($runtimeRequirement.FrameworkMajor)" "/DDotNetAllowsMajorRollForward=$([int]$runtimeRequirement.AllowsMajorRollForward)" "/DWindowsAppRuntimeUrl=$($windowsAppRuntime.source)" "/DWindowsAppRuntimeSha256=$($windowsAppRuntime.sha256)" "/DWindowsAppRuntimeMinimumVersion=$($windowsAppRuntime.requiredVersion)" "/DVcRedistUrl=$($vcRedist.source)" "/DVcRedistSha256=$($vcRedist.sha256)" "/DVcRedistMinimumVersion=$($vcRedist.minimumVersion)" (Join-Path $installerDirectory 'Quail.M09.FrameworkDependent.iss')
if ($LASTEXITCODE -ne 0) { throw 'Framework-dependent Inno Setup compilation failed.' }

Get-ChildItem -LiteralPath $outputDirectory -Filter '*.exe' | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "PASS installer=$($_.FullName) bytes=$($_.Length) sha256=$hash"
}
