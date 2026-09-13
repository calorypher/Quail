[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ScenarioPath,
    [string] $AppPath,
    [string] $OutputDirectory,
    [ValidateRange(1, 8)]
    [int] $BatchCount = 3,
    [ValidateRange(250, 10000)]
    [int] $InterQueryDelayMilliseconds = 1500,
    [switch] $CollectVsDiagnostics,
    [switch] $PersistentUi,
    [ValidateSet('settled-start', 'batch', 'idle')]
    [string] $PersistentPhase,
    [int] $QuailProcessId,
    [ValidateRange(1, 8)]
    [int] $BatchNumber,
    [ValidateSet(30, 120, 300)]
    [int] $IdleTargetSeconds
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

function Get-M16WorkloadQueries([string] $ResolvedScenarioPath) {
    $source = Get-Content -LiteralPath $ResolvedScenarioPath -Raw | ConvertFrom-Json
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

    $queries = [System.Collections.Generic.List[string]]::new()
    foreach ($id in $requiredIds) {
        foreach ($query in @($byId[$id].queries)) {
            $queries.Add([string]$query)
        }
    }

    return ,@($queries)
}

function Get-QuerySetFingerprint([string[]] $Queries) {
    $bytes = [Text.Encoding]::UTF8.GetBytes(($Queries -join "`n"))
    try {
        return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function Invoke-PersistentUiPhase([string] $RepositoryRoot) {
    if ([string]::IsNullOrWhiteSpace($PersistentPhase)) {
        throw 'Persistent UI measurement requires -PersistentPhase.'
    }
    if ($QuailProcessId -le 0) {
        throw 'Persistent UI measurement requires -QuailProcessId.'
    }
    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        throw 'Persistent UI measurement requires -OutputDirectory.'
    }
    if ($PersistentPhase -eq 'batch' -and $BatchNumber -le 0) {
        throw 'A persistent batch measurement requires -BatchNumber.'
    }
    if ($PersistentPhase -eq 'idle' -and $IdleTargetSeconds -le 0) {
        throw 'A persistent idle measurement requires -IdleTargetSeconds.'
    }
    if ($CollectVsDiagnostics) {
        throw 'Persistent UI measurement does not attach diagnostics. Run the equivalent profiler session separately.'
    }

    $process = Get-Process -Id $QuailProcessId -ErrorAction Stop
    if ($process.ProcessName -ne 'Quail') {
        throw "PID $QuailProcessId is '$($process.ProcessName)', not Quail."
    }

    $resolvedScenarioPath = (Resolve-Path -LiteralPath $ScenarioPath).Path
    $queries = [string[]](Get-M16WorkloadQueries $resolvedScenarioPath)
    $querySetFingerprint = Get-QuerySetFingerprint $queries
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $summaryPath = Join-Path $outputRoot 'persistent-resource-summary.json'
    $uiLogPath = Join-Path $outputRoot 'persistent-ui.private.log'

    if (Test-Path -LiteralPath $summaryPath) {
        $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
        if ([int]$summary.persistentProcessId -ne $QuailProcessId) {
            throw "Summary PID $($summary.persistentProcessId) does not match requested PID $QuailProcessId."
        }
        if ([string]$summary.querySetFingerprint -ne $querySetFingerprint) {
            throw 'The current scenario query set does not match the existing persistent-session summary.'
        }
        $gates = [System.Collections.Generic.List[object]]::new()
        foreach ($gate in @($summary.gates)) {
            $gates.Add($gate)
        }
    }
    else {
        $summary = [pscustomobject][ordered]@{
            schemaVersion = 1
            gitHead = (& git rev-parse HEAD).Trim()
            sourceDirty = [bool](@(& git status --porcelain=v1).Count -gt 0)
            persistentProcessId = $QuailProcessId
            queryCountPerBatch = $queries.Count
            querySetFingerprint = $querySetFingerprint
            interQueryDelayMilliseconds = $InterQueryDelayMilliseconds
            createdAtUtc = (Get-Date).ToUniversalTime().ToString('O')
            workloadCompletedAtUtc = $null
        }
        $gates = [System.Collections.Generic.List[object]]::new()
    }

    $gateName = switch ($PersistentPhase) {
        'settled-start' { 'settled-start' }
        'batch' { "post-batch-$BatchNumber" }
        'idle' { "idle-$($IdleTargetSeconds)s" }
    }
    if (@($gates | Where-Object { $_.gate -eq $gateName }).Count -ne 0) {
        throw "Gate '$gateName' has already been recorded."
    }

    if ($PersistentPhase -eq 'batch') {
        $winApp = 'C:\Users\gawry\AppData\Local\Microsoft\WindowsApps\winapp.exe'
        if (-not (Test-Path -LiteralPath $winApp -PathType Leaf)) {
            throw 'WinApp.exe was not found at the approved local path.'
        }

        foreach ($query in $queries) {
            & $winApp ui send-keys 'ctrl+a' -a $QuailProcessId --target QueryBox --via send-input --json *>> $uiLogPath
            if ($LASTEXITCODE -ne 0) {
                throw "WinApp failed to select the query field (exit $LASTEXITCODE)."
            }
            & $winApp ui send-keys --verbatim $query -a $QuailProcessId --target QueryBox --via send-input --json *>> $uiLogPath
            if ($LASTEXITCODE -ne 0) {
                throw "WinApp failed to type a private M16 query (exit $LASTEXITCODE)."
            }
            Start-Sleep -Milliseconds $InterQueryDelayMilliseconds
        }
    }
    elseif ($PersistentPhase -eq 'idle') {
        if ($null -eq $summary.workloadCompletedAtUtc) {
            throw 'Cannot record an idle gate before a workload batch completes.'
        }
        $workloadCompletedAtUtc = [datetime]::Parse([string]$summary.workloadCompletedAtUtc).ToUniversalTime()
        $remainingSeconds = [Math]::Max(0, $IdleTargetSeconds - ((Get-Date).ToUniversalTime() - $workloadCompletedAtUtc).TotalSeconds)
        while ($remainingSeconds -gt 0) {
            Start-Sleep -Seconds ([Math]::Min(30, [Math]::Ceiling($remainingSeconds)))
            $remainingSeconds = [Math]::Max(0, $IdleTargetSeconds - ((Get-Date).ToUniversalTime() - $workloadCompletedAtUtc).TotalSeconds)
        }
    }

    $sample = Get-ProcessSample $process ([datetime]::Parse([string]$summary.createdAtUtc).ToUniversalTime())
    $gate = [pscustomobject][ordered]@{
        gate = $gateName
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        idleSecondsSinceWorkload = if ($PersistentPhase -eq 'idle') { [Math]::Round(((Get-Date).ToUniversalTime() - [datetime]::Parse([string]$summary.workloadCompletedAtUtc).ToUniversalTime()).TotalSeconds, 3) } else { $null }
        sample = $sample
    }
    $gates.Add($gate)
    if ($PersistentPhase -eq 'batch') {
        $summary.workloadCompletedAtUtc = $gate.capturedAtUtc
    }
    $summary | Add-Member -NotePropertyName gates -NotePropertyValue @($gates) -Force
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $summaryPath -Encoding utf8
    Write-Output "PASS gate=$gateName summary=$summaryPath pid=$QuailProcessId"
}

Push-Location $repositoryRoot
try {
    if ($PersistentUi) {
        Invoke-PersistentUiPhase $repositoryRoot
        return
    }

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
