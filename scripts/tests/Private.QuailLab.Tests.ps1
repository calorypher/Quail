Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repositoryRoot 'scripts\Private.QuailLab.psm1') -Force

function Assert-Equal([object] $Actual, [object] $Expected, [string] $Message) {
    if ($Actual -ne $Expected) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("quail-checkpoint-test-{0}" -f [Guid]::NewGuid())
New-Item -ItemType Directory -Path $testDirectory | Out-Null

try {
    $successLog = Join-Path $testDirectory 'success.log'
    $successState = [pscustomobject]@{ Reads = 0; Clock = [datetime]'2026-08-21T12:00:00Z' }
    $created = [pscustomobject]@{ Name = 'M06-clean'; Id = [Guid]::Parse('8649f4e4-d6f1-464f-a7e8-320174119d72') }
    $result = Wait-QuailCheckpoint -VmName 'Test-VM' -CheckpointName 'M06-clean' -LogPath $successLog -TimeoutSeconds 5 -PollIntervalMilliseconds 1 `
        -SnapshotReader {
            param($unusedVmName)
            $successState.Reads++
            if ($successState.Reads -ge 3) { return @($created) }
            return @()
        } `
        -NowProvider { $successState.Clock } `
        -SleepAction { param($unusedMilliseconds) }
    Assert-Equal $successState.Reads 3 'Delayed checkpoint visibility should be retried.'
    Assert-Equal $result.Id $created.Id 'The visible checkpoint should be returned.'
    if ((Get-Content -LiteralPath $successLog -Raw) -notmatch 'attempts=3 count=1') {
        throw 'Successful checkpoint verification did not record its final attempt count.'
    }

    $timeoutLog = Join-Path $testDirectory 'timeout.log'
    $timeoutState = [pscustomobject]@{ Reads = 0; Clock = [datetime]'2026-08-21T12:00:00Z' }
    try {
        Wait-QuailCheckpoint -VmName 'Test-VM' -CheckpointName 'M06-clean' -LogPath $timeoutLog -TimeoutSeconds 1 -PollIntervalMilliseconds 1 `
            -SnapshotReader {
                param($unusedVmName)
                $timeoutState.Reads++
                return @()
            } `
            -NowProvider {
                $now = $timeoutState.Clock
                $timeoutState.Clock = $timeoutState.Clock.AddSeconds(2)
                return $now
            } `
            -SleepAction { param($unusedMilliseconds) } | Out-Null
        throw 'Expected bounded checkpoint verification to time out.'
    }
    catch {
        if ($_.Exception.Message -notmatch '^checkpoint-create-verification-timed-out name=M06-clean') {
            throw
        }
    }
    Assert-Equal $timeoutState.Reads 1 'Timeout verification should not loop after its deadline.'

    $duplicateLog = Join-Path $testDirectory 'duplicate.log'
    try {
        Wait-QuailCheckpoint -VmName 'Test-VM' -CheckpointName 'M06-clean' -LogPath $duplicateLog -TimeoutSeconds 5 -PollIntervalMilliseconds 1 `
            -SnapshotReader {
                param($unusedVmName)
                return @(
                    [pscustomobject]@{ Name = 'M06-clean'; Id = [Guid]::NewGuid() },
                    [pscustomobject]@{ Name = 'M06-clean'; Id = [Guid]::NewGuid() }
                )
            } `
            -NowProvider { [datetime]'2026-08-21T12:00:00Z' } `
            -SleepAction { param($unusedMilliseconds) } | Out-Null
        throw 'Expected duplicate matching checkpoints to fail.'
    }
    catch {
        if ($_.Exception.Message -notmatch '^checkpoint-create-verification-failed name=M06-clean attempts=1 count=2') {
            throw
        }
    }

    Write-Output 'PASS Private.QuailLab checkpoint polling tests'
}
finally {
    Remove-Item -LiteralPath $testDirectory -Recurse -Force -ErrorAction SilentlyContinue
}
