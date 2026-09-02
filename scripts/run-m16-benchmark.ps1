[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ScenarioPath,
    [string[]] $ScenarioId,
    [string[]] $IndexPath,
    [string] $AppPath,
    [string] $OutputDirectory,
    [ValidateRange(1, 10)]
    [int] $Repetitions = 1,
    [ValidateRange(10, 120)]
    [int] $ScenarioTimeoutSeconds = 45,
    [switch] $NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Quote-ProcessArgument([string] $Value) {
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Get-TraceStage($Events, [string] $Stage, [long] $UiGeneration) {
    return $Events | Where-Object { $_.stage -eq $Stage -and $_.uiGeneration -eq $UiGeneration } | Select-Object -First 1
}

function Get-StageDurationMilliseconds($Events, [string] $Stage, [long] $UiGeneration) {
    $event = Get-TraceStage $Events $Stage $UiGeneration
    if ($null -eq $event) {
        return $null
    }

    return [double] $event.durationMilliseconds
}

function Get-Median([double[]] $Values) {
    $ordered = @($Values | Sort-Object)
    if ($ordered.Count -eq 0) {
        return $null
    }

    $middle = [int]($ordered.Count / 2)
    if ($ordered.Count % 2 -eq 1) {
        return $ordered[$middle]
    }

    return ($ordered[$middle - 1] + $ordered[$middle]) / 2
}

Push-Location $repositoryRoot
try {
    $resolvedScenarioPath = (Resolve-Path -LiteralPath $ScenarioPath).Path
    $scenarioDocument = Get-Content -LiteralPath $resolvedScenarioPath -Raw | ConvertFrom-Json
    if ($scenarioDocument.schemaVersion -ne 1 -or @($scenarioDocument.scenarios).Count -eq 0) {
        throw 'Scenario file must use schemaVersion 1 and contain scenarios.'
    }

    $scenarios = @($scenarioDocument.scenarios)
    if ($ScenarioId.Count -gt 0) {
        $requested = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $ScenarioId | ForEach-Object { [void]$requested.Add($_) }
        $scenarios = @($scenarios | Where-Object { $requested.Contains([string]$_.id) })
        if ($scenarios.Count -ne $requested.Count) {
            throw 'One or more -ScenarioId values are absent from the scenario file.'
        }
    }

    foreach ($scenario in $scenarios) {
        if ([string]::IsNullOrWhiteSpace($scenario.id) -or @($scenario.queries).Count -eq 0) {
            throw 'Every selected scenario requires an id and at least one query.'
        }
        if ($scenario.sessionKind -notin @('fresh-process-first-search', 'warm-same-session')) {
            throw "Scenario '$($scenario.id)' has an unsupported sessionKind."
        }
        $warmupQueries = @()
        if ($null -ne $scenario.PSObject.Properties['warmupQueries']) {
            $warmupQueries = @($scenario.warmupQueries)
        }
        if ($scenario.sessionKind -eq 'warm-same-session' -and $warmupQueries.Count -eq 0) {
            throw "Scenario '$($scenario.id)' is warm-same-session and requires at least one warmup query."
        }
        if ($scenario.sessionKind -eq 'fresh-process-first-search' -and $warmupQueries.Count -ne 0) {
            throw "Scenario '$($scenario.id)' is fresh-process-first-search and must not define warmup queries."
        }
    }

    if (-not $NoBuild) {
        & dotnet build src/Quail.App/Quail.App.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) {
            throw "Release build failed with exit code $LASTEXITCODE."
        }
    }

    if ([string]::IsNullOrWhiteSpace($AppPath)) {
        $appCandidate = Get-ChildItem -Path (Join-Path $repositoryRoot 'src/Quail.App/bin/Release') -Filter Quail.exe -Recurse |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -eq $appCandidate) {
            throw 'Quail.exe was not found under the Release build output. Run without -NoBuild or provide -AppPath.'
        }
        $AppPath = $appCandidate.FullName
    }
    $resolvedAppPath = (Resolve-Path -LiteralPath $AppPath).Path

    if (Get-Process -Name Quail -ErrorAction SilentlyContinue) {
        throw 'Exit the resident Quail process before running the benchmark. The harness does not take over an existing desktop session.'
    }

    if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
        $OutputDirectory = Join-Path $repositoryRoot (Join-Path 'artifacts/m16' (Get-Date -Format 'yyyyMMdd-HHmmss'))
    }
    $outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $driverDirectory = Join-Path $outputRoot 'drivers'
    New-Item -ItemType Directory -Path $driverDirectory -Force | Out-Null

    $samples = [System.Collections.Generic.List[object]]::new()
    foreach ($scenario in $scenarios) {
        for ($iteration = 1; $iteration -le $Repetitions; $iteration++) {
            $runName = '{0}-{1:D2}' -f $scenario.id, $iteration
            $driverPath = Join-Path $driverDirectory "$runName.json"
            $warmupQueries = @()
            if ($null -ne $scenario.PSObject.Properties['warmupQueries']) {
                $warmupQueries = @($scenario.warmupQueries)
            }
            $interQueryDelayMilliseconds = if ($null -eq $scenario.PSObject.Properties['interQueryDelayMilliseconds']) { 0 } else { [int]$scenario.interQueryDelayMilliseconds }
            [ordered]@{
                schemaVersion = 1
                id = [string]$scenario.id
                sessionKind = [string]$scenario.sessionKind
                warmupQueries = $warmupQueries
                queries = @($scenario.queries)
                interQueryDelayMilliseconds = $interQueryDelayMilliseconds
            } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $driverPath -Encoding utf8

            $tracePath = Join-Path $outputRoot "$runName.trace.jsonl"
            $diagnosticsPath = Join-Path $outputRoot "$runName.diagnostics.log"
            $arguments = @(
                '--show-on-start',
                '--diagnostics-path', $diagnosticsPath,
                '--search-performance-trace', $tracePath,
                '--search-performance-session-kind', [string]$scenario.sessionKind,
                '--search-performance-scenario', $driverPath)
            foreach ($index in @($IndexPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
                $arguments += @('--index', [IO.Path]::GetFullPath($index))
            }

            $process = Start-Process -FilePath $resolvedAppPath -ArgumentList (($arguments | ForEach-Object { Quote-ProcessArgument $_ }) -join ' ') -PassThru
            if (-not $process.WaitForExit($ScenarioTimeoutSeconds * 1000)) {
                throw "Scenario '$($scenario.id)' did not exit within $ScenarioTimeoutSeconds seconds. The harness left the child process running for inspection."
            }
            if (-not (Test-Path -LiteralPath $tracePath)) {
                throw "Scenario '$($scenario.id)' produced no trace."
            }

            $events = @(Get-Content -LiteralPath $tracePath | ForEach-Object { $_ | ConvertFrom-Json })
            $scenarioStart = @($events | Where-Object { $_.stage -eq 'scenario-start' -and $_.scenarioId -eq $scenario.id } | Select-Object -First 1)
            $scenarioFailure = @($events | Where-Object { $_.stage -eq 'scenario-failed' } | Select-Object -First 1)
            if ($scenarioStart.Count -eq 0 -or $scenarioFailure.Count -ne 0) {
                throw "Scenario '$($scenario.id)' did not complete successfully."
            }

            $inputs = @($events | Where-Object { $_.stage -eq 'input-observed' -and $_.scenarioId -eq $scenario.id -and $_.queryLength -gt 0 })
            if ($inputs.Count -eq 0) {
                throw "Scenario '$($scenario.id)' produced no non-empty input event."
            }
            $finalInput = $inputs[-1]
            $typingBurstInput = if (@($scenario.queries).Count -gt 1) { $inputs[0] } else { $null }
            $firstRender = Get-TraceStage $events 'first-text-results-rendered' ([long]$finalInput.uiGeneration)
            if ($null -eq $firstRender) {
                throw "Scenario '$($scenario.id)' did not produce first-text render evidence for its final query."
            }
            $coreStarted = Get-TraceStage $events 'core-search-started' ([long]$finalInput.uiGeneration)
            $sessionStart = @($events | Where-Object { $_.stage -eq 'session-start' } | Select-Object -First 1)

            $samples.Add([pscustomobject][ordered]@{
                scenarioId = [string]$scenario.id
                iteration = $iteration
                sessionKind = [string]$scenario.sessionKind
                finalQueryLength = [int]$finalInput.queryLength
                resultCount = [int]$firstRender.resultCount
                inputToFirstTextMilliseconds = [Math]::Round([double]$firstRender.monotonicMilliseconds - [double]$finalInput.monotonicMilliseconds, 3)
                typingBurstToFirstTextMilliseconds = if ($null -eq $typingBurstInput) { $null } else { [Math]::Round([double]$firstRender.monotonicMilliseconds - [double]$typingBurstInput.monotonicMilliseconds, 3) }
                queueWaitMilliseconds = if ($null -eq $coreStarted) { $null } else { [Math]::Round([double]$coreStarted.queueWaitMilliseconds, 3) }
                coreSearchMilliseconds = Get-StageDurationMilliseconds $events 'core-search-completed' ([long]$finalInput.uiGeneration)
                resultMappingMilliseconds = Get-StageDurationMilliseconds $events 'result-mapping-completed' ([long]$finalInput.uiGeneration)
                resultApplyMilliseconds = Get-StageDurationMilliseconds $events 'result-apply-completed' ([long]$finalInput.uiGeneration)
                sourceStatusMilliseconds = Get-StageDurationMilliseconds $events 'source-status-completed' ([long]$finalInput.uiGeneration)
                indexCount = if ($sessionStart.Count -eq 0) { $null } else { $sessionStart[0].indexCount }
                recordCount = if ($sessionStart.Count -eq 0) { $null } else { $sessionStart[0].recordCount }
                databaseBytes = if ($sessionStart.Count -eq 0) { $null } else { $sessionStart[0].databaseBytes }
                traceFile = [IO.Path]::GetFileName($tracePath)
            })
        }
    }

    $environment = [ordered]@{
        capturedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        gitHead = (& git rev-parse HEAD).Trim()
        sourceDirty = [bool](@(& git status --porcelain=v1).Count -gt 0)
        dotnetVersion = (& dotnet --version).Trim()
        osVersion = [Environment]::OSVersion.VersionString
        appFile = [IO.Path]::GetFileName($resolvedAppPath)
        repetitions = $Repetitions
    }
    [ordered]@{
        schemaVersion = 1
        environment = $environment
        samples = @($samples)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $outputRoot 'results.json') -Encoding utf8

    $summaryLines = @(
        "M16 benchmark summary",
        "git=$($environment.gitHead) sourceDirty=$($environment.sourceDirty) repetitions=$Repetitions",
        "scenario                         median input-to-text ms  median typing-burst ms  samples")
    foreach ($group in @($samples | Group-Object scenarioId)) {
        $inputMedian = Get-Median ([double[]]@($group.Group | ForEach-Object { $_.inputToFirstTextMilliseconds }))
        $typingValues = [double[]]@($group.Group | Where-Object { $null -ne $_.typingBurstToFirstTextMilliseconds } | ForEach-Object { $_.typingBurstToFirstTextMilliseconds })
        $typingMedian = Get-Median $typingValues
        $typingText = if ($null -eq $typingMedian) { 'n/a' } else { '{0:N3}' -f $typingMedian }
        $summaryLines += ('{0,-32} {1,23:N3} {2,24} {3,8}' -f $group.Name, $inputMedian, $typingText, $group.Count)
    }
    $summaryLines | Set-Content -LiteralPath (Join-Path $outputRoot 'summary.txt') -Encoding utf8
    Write-Output "PASS results=$(Join-Path $outputRoot 'results.json') summary=$(Join-Path $outputRoot 'summary.txt')"
}
finally {
    Pop-Location
}
