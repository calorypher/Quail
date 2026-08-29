[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('self-contained', 'framework-dependent')]
    [string] $Variant,
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $FixtureVersion = '0.9.0',
    [ValidateRange(1, 100)]
    [int] $Warmups = 5,
    [ValidateRange(1, 100)]
    [int] $Runs = 30,
    [ValidateRange(1, 60)]
    [int] $IdleSamples = 10,
    [switch] $IdleOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$application = Join-Path $repositoryRoot "artifacts\m09\publish\$Variant-$FixtureVersion\Quail.M08.WinUi.exe"
$outputDirectory = Join-Path $repositoryRoot "artifacts\m09\host-measurements\$Variant-$FixtureVersion"

if (-not (Test-Path -LiteralPath $application -PathType Leaf)) {
    throw "Publish executable is missing: $application"
}

New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

function Wait-M08Event {
    param(
        [Parameter(Mandatory)] [System.IO.Pipes.NamedPipeServerStream] $Server,
        [Parameter(Mandatory)] [string] $ExpectedEvent,
        [Parameter(Mandatory)] [TimeSpan] $Timeout
    )

    if (-not $Server.WaitForConnectionAsync().Wait($Timeout)) {
        throw "Timed out waiting for fixture pipe connection."
    }

    $reader = [System.IO.StreamReader]::new($Server)
    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $remaining = [Math]::Max(1, [int]($deadline - [DateTimeOffset]::UtcNow).TotalMilliseconds)
        $lineTask = $reader.ReadLineAsync()
        if (-not $lineTask.Wait($remaining)) {
            break
        }

        $line = $lineTask.Result
        if ($null -eq $line) {
            break
        }

        $payload = $line | ConvertFrom-Json
        if ($payload.event -eq $ExpectedEvent) {
            return $payload
        }
    }

    throw "Timed out waiting for fixture event '$ExpectedEvent'."
}

function Stop-Fixture {
    param([Parameter(Mandatory)] [System.Diagnostics.Process] $Process)

    if (-not $Process.HasExited) {
        $Process.Kill()
        $Process.WaitForExit()
    }
}

function Invoke-StartupMeasurement {
    param([Parameter(Mandatory)] [int] $Run, [Parameter(Mandatory)] [string] $Phase)

    $pipeName = "quail-m09-$Variant-$Phase-$Run-$([Guid]::NewGuid().ToString('N'))"
    $server = [System.IO.Pipes.NamedPipeServerStream]::new($pipeName, [System.IO.Pipes.PipeDirection]::In, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $process = Start-Process -FilePath $application -ArgumentList @('--m08-pipe', $pipeName, '--m08-show-on-start', '--m08-test-exit-after-visible-ready-count', '1') -PassThru
        try {
            $event = Wait-M08Event -Server $server -ExpectedEvent 'visible-ready' -Timeout ([TimeSpan]::FromSeconds(12))
            $stopwatch.Stop()
            if (-not $process.WaitForExit(5000)) {
                throw "Fixture did not exit after visible-ready."
            }

            return [pscustomobject]@{
                variant = $Variant
                scenario = 'startup'
                phase = $Phase
                run = $Run
                milliseconds = [Math]::Round($stopwatch.Elapsed.TotalMilliseconds, 3)
                status = 'pass'
                event = $event.event
                workingSetBytes = $null
                privateBytes = $null
                cpuPercent = $null
            }
        }
        finally {
            Stop-Fixture $process
        }
    }
    finally {
        $server.Dispose()
    }
}

$measurements = @()
if (-not $IdleOnly) {
    for ($run = 1; $run -le $Warmups; $run++) {
        $measurements += Invoke-StartupMeasurement -Run $run -Phase 'warmup'
    }
    for ($run = 1; $run -le $Runs; $run++) {
        $measurements += Invoke-StartupMeasurement -Run $run -Phase 'measured'
    }
}

$pipeName = "quail-m09-$Variant-idle-$([Guid]::NewGuid().ToString('N'))"
$server = [System.IO.Pipes.NamedPipeServerStream]::new($pipeName, [System.IO.Pipes.PipeDirection]::In, 1, [System.IO.Pipes.PipeTransmissionMode]::Byte, [System.IO.Pipes.PipeOptions]::Asynchronous)
try {
    $fixture = Start-Process -FilePath $application -ArgumentList @('--m08-pipe', $pipeName) -PassThru
    try {
        $null = Wait-M08Event -Server $server -ExpectedEvent 'startup-hidden' -Timeout ([TimeSpan]::FromSeconds(12))
        Start-Sleep -Seconds 3
        $previousCpu = $fixture.TotalProcessorTime
        $previousTimestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
        for ($sample = 1; $sample -le $IdleSamples; $sample++) {
            Start-Sleep -Seconds 1
            $fixture.Refresh()
            $timestamp = [System.Diagnostics.Stopwatch]::GetTimestamp()
            $elapsed = ($timestamp - $previousTimestamp) / [System.Diagnostics.Stopwatch]::Frequency
            $cpuDelta = ($fixture.TotalProcessorTime - $previousCpu).TotalSeconds
            $cpuPercent = 100 * $cpuDelta / ($elapsed * [Environment]::ProcessorCount)
            $measurements += [pscustomobject]@{
                variant = $Variant
                scenario = 'hidden-idle'
                phase = 'measured'
                run = $sample
                milliseconds = $null
                status = 'observed'
                workingSetBytes = $fixture.WorkingSet64
                privateBytes = $fixture.PrivateMemorySize64
                cpuPercent = [Math]::Round($cpuPercent, 4)
            }
            $previousCpu = $fixture.TotalProcessorTime
            $previousTimestamp = $timestamp
        }
    }
    finally {
        Stop-Fixture $fixture
    }
}
finally {
    $server.Dispose()
}

$csv = Join-Path $outputDirectory $(if ($IdleOnly) { 'idle-measurements.csv' } else { 'measurements.csv' })
$measurements | Export-Csv -LiteralPath $csv -NoTypeInformation
$startup = @($measurements | Where-Object { $_.scenario -eq 'startup' -and $_.phase -eq 'measured' } | Select-Object -ExpandProperty milliseconds | Sort-Object)
$idle = @($measurements | Where-Object { $_.scenario -eq 'hidden-idle' })
$p50Index = if ($startup.Count -gt 0) { [Math]::Ceiling(0.50 * $startup.Count) - 1 } else { 0 }
$p95Index = if ($startup.Count -gt 0) { [Math]::Ceiling(0.95 * $startup.Count) - 1 } else { 0 }
$summary = [pscustomobject]@{
    variant = $Variant
    startup = [pscustomobject]@{
        runs = $startup.Count
        p50Milliseconds = if ($startup.Count -gt 0) { [Math]::Round($startup[$p50Index], 3) } else { $null }
        p95Milliseconds = if ($startup.Count -gt 0) { [Math]::Round($startup[$p95Index], 3) } else { $null }
        maxMilliseconds = if ($startup.Count -gt 0) { [Math]::Round(($startup | Measure-Object -Maximum).Maximum, 3) } else { $null }
        averageMilliseconds = if ($startup.Count -gt 0) { [Math]::Round(($startup | Measure-Object -Average).Average, 3) } else { $null }
    }
    hiddenIdle = [pscustomobject]@{
        samples = $idle.Count
        averageWorkingSetBytes = [Math]::Round(($idle | Measure-Object -Property workingSetBytes -Average).Average, 0)
        averagePrivateBytes = [Math]::Round(($idle | Measure-Object -Property privateBytes -Average).Average, 0)
        averageCpuPercent = [Math]::Round(($idle | Measure-Object -Property cpuPercent -Average).Average, 4)
        maxCpuPercent = [Math]::Round(($idle | Measure-Object -Property cpuPercent -Maximum).Maximum, 4)
    }
    csv = $csv
}

$summary | ConvertTo-Json -Compress -Depth 4
