[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ScenarioPath,
    [string] $AppPath,
    [string] $OutputDirectory,
    [ValidateRange(1, 5)]
    [int] $BatchCount = 3,
    [ValidateRange(250, 10000)]
    [int] $InterQueryDelayMilliseconds = 1500,
    [switch] $CollectVsDiagnostics
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$vsDiagnostics = 'C:\Program Files\Microsoft Visual Studio\18\Community\Team Tools\DiagnosticsHub\Collector\VSDiagnostics.exe'
$vsCountersConfig = 'C:\Program Files\Microsoft Visual Studio\18\Community\Team Tools\DiagnosticsHub\Collector\AgentConfigs\DotNetCountersBase.json'

function Quote-ProcessArgument([string] $Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Get-ProcessSample([System.Diagnostics.Process] $Process, [datetime] $StartedAtUtc) {
    $Process.Refresh()
    return [pscustomobject][ordered]@{
        elapsedMilliseconds = [Math]::Round(((Get-Date).ToUniversalTime() - $StartedAtUtc).TotalMilliseconds, 3)
        workingSetBytes = $Process.WorkingSet64
        privateBytes = $Process.PrivateMemorySize64
        pagedMemoryBytes = $Process.PagedMemorySize64
        handleCount = $Process.HandleCount
        threadCount = $Process.Threads.Count
        totalCpuMilliseconds = [Math]::Round($Process.TotalProcessorTime.TotalMilliseconds, 3)
    }
}

Push-Location $repositoryRoot
try {
    if (Get-Process -Name Quail -ErrorAction SilentlyContinue) {
        throw 'Exit the resident Quail process before collecting M24 resource evidence.'
    }

    $resolvedScenarioPath = (Resolve-Path -LiteralPath $ScenarioPath).Path
    $source = Get-Content -LiteralPath $resolvedScenarioPath -Raw | ConvertFrom-Json
    $requiredIds = @('ordinary-name', 'strong-prefix', 'broad-result', 'one-character', 'two-character', 'warm-repeated', 'fresh-process-first-search', 'rapid-typing')
    $byId = @{}
    foreach ($scenario in @($source.scenarios)) {
        $byId[[string]$scenario.id] = $scenario
    }
    foreach ($id in $requiredIds) {
        if (-not $byId.ContainsKey($id)) {
            throw "Scenario input is missing required M16 scenario '$id'."
        }
    }

    if ([string]::IsNullOrWhiteSpace($AppPath)) {
        $candidate = Get-ChildItem -Path (Join-Path $repositoryRoot 'src\Quail.App\bin\Release') -Filter Quail.exe -Recurse |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $candidate) {
            throw 'Quail.exe was not found under the Release build output. Build the Release App first or provide -AppPath.'
        }
        $AppPath = $candidate.FullName
    }
    $resolvedAppPath = (Resolve-Path -LiteralPath $AppPath).Path

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot (Join-Path 'artifacts\m24-b' (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

    $workloadQueries = [System.Collections.Generic.List[string]]::new()
    for ($batch = 1; $batch -le $BatchCount; $batch++) {
        foreach ($id in $requiredIds) {
            foreach ($query in @($byId[$id].queries)) {
                $workloadQueries.Add([string]$query)
            }
        }
    }
    $workloadPath = Join-Path $outputRoot 'resource-workload.private.json'
    [ordered]@{
        schemaVersion = 1
        id = 'm24-resource-workload'
        sessionKind = 'warm-same-session'
        warmupQueries = @($byId['ordinary-name'].warmupQueries)
        queries = @($workloadQueries)
        interQueryDelayMilliseconds = $InterQueryDelayMilliseconds
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $workloadPath -Encoding utf8

    $tracePath = Join-Path $outputRoot 'resource-workload.trace.jsonl'
    $diagnosticsPath = Join-Path $outputRoot 'resource-workload.diagnostics.log'
    $arguments = @(
        '--show-on-start',
        '--diagnostics-path', $diagnosticsPath,
        '--search-performance-trace', $tracePath,
        '--search-performance-session-kind', 'warm-same-session',
        '--search-performance-scenario', $workloadPath)
    $process = Start-Process -FilePath $resolvedAppPath -ArgumentList (($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' ') -PassThru
    $startedAtUtc = (Get-Date).ToUniversalTime()
    $samples = [System.Collections.Generic.List[object]]::new()
    $sessionId = [guid]::NewGuid().ToString()
    $vsOutputPath = Join-Path $outputRoot 'resource-workload.diagsession'
    $vsStarted = $false

    try {
        if ($CollectVsDiagnostics) {
            if (-not (Test-Path -LiteralPath $vsDiagnostics) -or -not (Test-Path -LiteralPath $vsCountersConfig)) {
                throw 'Visual Studio .NET Counters collector is unavailable.'
            }
            & $vsDiagnostics start $sessionId "/attach:$($process.Id)" "/loadConfig:$vsCountersConfig" "/scratchLocation:$outputRoot" /package:dir
            if ($LASTEXITCODE -ne 0) {
                throw "Visual Studio .NET Counters collector failed to attach (exit $LASTEXITCODE)."
            }
            $vsStarted = $true
        }

        while (-not $process.HasExited) {
            $samples.Add((Get-ProcessSample $process $startedAtUtc))
            Start-Sleep -Milliseconds 500
            $process.Refresh()
        }
    }
    finally {
        if ($vsStarted) {
            & $vsDiagnostics stop $sessionId "/output:$vsOutputPath"
            if ($LASTEXITCODE -ne 0) {
                throw "Visual Studio .NET Counters collector failed to stop (exit $LASTEXITCODE)."
            }
        }
        $process.Dispose()
    }

    $traceEvents = @(Get-Content -LiteralPath $tracePath | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($traceEvents | Where-Object { $_.stage -eq 'scenario-completed' }).Count -ne 1 -or
        @($traceEvents | Where-Object { $_.stage -eq 'scenario-failed' }).Count -ne 0) {
        throw 'The M24 resource workload did not complete successfully.'
    }

    [ordered]@{
        schemaVersion = 1
        gitHead = (& git rev-parse HEAD).Trim()
        sourceDirty = [bool](@(& git status --porcelain=v1).Count -gt 0)
        capturedAtUtc = $startedAtUtc.ToString('O')
        appFile = [IO.Path]::GetFileName($resolvedAppPath)
        batchCount = $BatchCount
        queryCount = $workloadQueries.Count
        interQueryDelayMilliseconds = $InterQueryDelayMilliseconds
        visualStudioCountersCollected = $vsStarted
        samples = @($samples)
        traceAnchors = @($traceEvents | Where-Object { $_.stage -in @('session-start', 'first-text-results-rendered', 'scenario-completed') } |
            Select-Object stage, monotonicMilliseconds, uiGeneration, resultCount, processCpuMilliseconds, workingSetBytes)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'resource-summary.json') -Encoding utf8

    Write-Output "PASS summary=$(Join-Path $outputRoot 'resource-summary.json') diagnostics=$vsStarted"
}
finally {
    Pop-Location
}
