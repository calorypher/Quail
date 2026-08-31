[CmdletBinding()]
param(
    [string] $Output = 'artifacts\m10\publish\self-contained'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\Quail.App\Quail.App.csproj'
$publishOutput = Join-Path $repositoryRoot $Output
$buildOutput = Join-Path $repositoryRoot 'src\Quail.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64'

& dotnet publish $project `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $publishOutput `
    --no-restore `
    -p:Platform=x64 `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -p:PublishReadyToRun=false `
    -p:PublishAot=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

foreach ($artifact in @(
    'App.xbf',
    'QuickSearchWindow.xbf',
    'Quail.pri',
    'Assets\quail-feather-A-gradient.svg',
    'Assets\quail-app-icon-32px.png',
    'Assets\quail-app-icon-48px.png',
    'Assets\quail-tray-icon-16px.png')) {
    $source = Join-Path $buildOutput $artifact
    $destination = Join-Path $publishOutput $artifact

    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "The M10 Release build is missing required unpackaged WinUI artifact: $source"
    }

    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

$requiredArtifacts = @(
    'Quail.exe',
    'Quail.FileSystem.dll',
    'App.xbf',
    'QuickSearchWindow.xbf',
    'Quail.pri',
    'Assets\quail-feather-A-gradient.svg',
    'Assets\quail-app-icon-32px.png',
    'Assets\quail-app-icon-48px.png',
    'Assets\quail-tray-icon-16px.png',
    'Microsoft.Data.Sqlite.dll',
    'SQLitePCLRaw.batteries_v2.dll',
    'SQLitePCLRaw.core.dll',
    'SQLitePCLRaw.provider.e_sqlite3.dll',
    'e_sqlite3.dll')
foreach ($artifact in $requiredArtifacts) {
    $path = Join-Path $publishOutput $artifact
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Publish output is missing required M10 artifact: $path"
    }
}

$files = @(Get-ChildItem -LiteralPath $publishOutput -Recurse -File)
$bytes = ($files | Measure-Object -Property Length -Sum).Sum
Write-Output "PASS output=$publishOutput fileCount=$($files.Count) bytes=$bytes executable=$(Join-Path $publishOutput 'Quail.exe')"
