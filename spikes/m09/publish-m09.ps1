[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('self-contained', 'framework-dependent')]
    [string] $Variant,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $FixtureVersion = '0.9.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$project = Join-Path $repositoryRoot 'spikes\m08\winui\Quail.M08.WinUi.csproj'
$output = Join-Path $repositoryRoot "artifacts\m09\publish\$Variant-$FixtureVersion"

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

New-Item -ItemType Directory -Path $output -Force | Out-Null

$arguments = @(
    'publish', $project,
    '--configuration', 'Release',
    '--runtime', 'win-x64',
    '--output', $output,
    '-p:WindowsPackageType=None',
    '-p:PublishSingleFile=false',
    '-p:PublishTrimmed=false',
    '-p:PublishReadyToRun=false',
    '-p:PublishAot=false',
    "-p:Version=$FixtureVersion")

if ($Variant -eq 'self-contained') {
    $arguments += '--self-contained'
    $arguments += 'true'
    $arguments += '-p:WindowsAppSDKSelfContained=true'
}
else {
    $arguments += '--self-contained'
    $arguments += 'false'
    $arguments += '-p:WindowsAppSDKSelfContained=false'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$buildOutput = Join-Path $repositoryRoot 'spikes\m08\winui\bin\Release\net10.0-windows10.0.26100.0\win-x64'
foreach ($artifact in @('App.xbf', 'MainWindow.xbf', 'Quail.M08.WinUi.pri')) {
    $source = Join-Path $buildOutput $artifact
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "The M08 Release build is missing required unpackaged WinUI artifact: $source"
    }

    Copy-Item -LiteralPath $source -Destination (Join-Path $output $artifact) -Force
}

$executable = Join-Path $output 'Quail.M08.WinUi.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Publish output is missing $executable."
}

$files = @(Get-ChildItem -LiteralPath $output -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output "PASS variant=$Variant version=$FixtureVersion output=$output fileCount=$($files.Count) bytes=$bytes executable=$executable"
