[CmdletBinding()]
param(
    [string] $OutputDirectory,
    [string] $BenchmarkRoot = (Join-Path $env:ProgramData 'Quail\Benchmarks\M17.6'),
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$principal = [Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'M17.6 requires an elevated PowerShell session for the physical C: NTFS benchmark.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot (Join-Path 'artifacts\m17.6' (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$benchmarkRunId = Get-Date -Format 'yyyyMMdd-HHmmss'
$databaseDirectory = Join-Path $BenchmarkRoot $benchmarkRunId
$databasePath = Join-Path $databaseDirectory 'c-index.db'
$projectPath = Join-Path $repositoryRoot 'src\Quail.BuildBenchmark\Quail.BuildBenchmark.csproj'
$dllPath = Join-Path $repositoryRoot 'src\Quail.BuildBenchmark\bin\Release\net10.0-windows\Quail.BuildBenchmark.dll'

Push-Location $repositoryRoot
try {
    if (-not $NoBuild) {
        & dotnet build $projectPath --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Release benchmark build failed with exit code $LASTEXITCODE."
        }
    }

    if (-not (Test-Path -LiteralPath $dllPath)) {
        throw 'The Release benchmark helper was not found. Run without -NoBuild.'
    }

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $databaseDirectory -Force | Out-Null
    $gitHead = (& git rev-parse HEAD).Trim()
    $sourceDirty = [bool](@(& git status --porcelain=v1).Count -gt 0)
    $dotnetVersion = (& dotnet --version).Trim()
    $runs = @(
        [ordered]@{ kind = 'warmup'; number = 0 },
        [ordered]@{ kind = 'measured'; number = 1 },
        [ordered]@{ kind = 'measured'; number = 2 },
        [ordered]@{ kind = 'measured'; number = 3 })

    $runFiles = [System.Collections.Generic.List[string]]::new()
    foreach ($run in $runs) {
        $benchmarkArguments = @(
            '--database-path', $databasePath,
            '--output-directory', $outputRoot,
            '--mount-point', 'C:\',
            '--run-number', $run.number,
            '--run-kind', $run.kind,
            '--git-head', $gitHead,
            '--source-dirty', $sourceDirty,
            '--dotnet-version', $dotnetVersion)
        & dotnet $dllPath $benchmarkArguments
        if ($LASTEXITCODE -ne 0) {
            throw "Benchmark $($run.kind)-$($run.number) failed with exit code $LASTEXITCODE."
        }

        $runFiles.Add(('{0}-{1:D2}.json' -f $run.kind, $run.number))
    }

    [ordered]@{
        schemaVersion = 1
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        canonicalCampaign = $true
        databaseLocation = 'ProgramData/Quail/Benchmarks/M17.6/<run>/c-index.db'
        runFiles = @($runFiles)
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputRoot 'manifest.json') -Encoding utf8

    Write-Output "PASS results=$outputRoot database=$databasePath"
}
finally {
    Pop-Location
}
