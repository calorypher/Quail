[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string] $Milestone,
    [string] $CheckpointName,
    [Parameter(Mandatory)]
    [string] $BranchName,
    [Parameter(Mandatory)]
    [string] $VmUser,
    [Parameter(Mandatory)]
    [string] $VmRepositoryPath,
    [string] $VmName = 'Quail-Lab',
    [string] $DataVolumeLabel = 'QUAIL_LAB_DATA',
    [ValidateRange(1, 600)]
    [int] $TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Private.QuailLab.psm1') -Force
$logPath = New-QuailToolingLog $repositoryRoot 'prepare-milestone'
$isWhatIf = [bool]$WhatIfPreference

function Fail([string] $Reason) {
    $compactReason = ($Reason -replace '[\r\n]+', ' ').Trim()
    Write-Output "FAIL reason=$compactReason log=$logPath"
    exit 1
}

function Invoke-Git([string[]] $Arguments) {
    return Invoke-QuailLoggedExternal $logPath 'git' $Arguments
}

function Get-GitRelationship([string] $Left, [string] $Right) {
    $parts = (Invoke-Git @('rev-list', '--left-right', '--count', "$Left...$Right") | Select-Object -Last 1).Trim().Split([char[]]" `t", [StringSplitOptions]::RemoveEmptyEntries)
    if ($parts.Count -ne 2) {
        throw "Could not determine relationship between $Left and $Right."
    }

    return [pscustomobject]@{ Ahead = [int]$parts[0]; Behind = [int]$parts[1] }
}

function Assert-BranchAvailable {
    Invoke-Git @('check-ref-format', '--branch', $BranchName) | Out-Null
    $local = & git show-ref --verify --quiet "refs/heads/$BranchName"
    if ($LASTEXITCODE -eq 0) {
        throw "Branch already exists locally: $BranchName"
    }
    if ($LASTEXITCODE -ne 1) {
        throw "Could not inspect local branch $BranchName."
    }

    $remote = & git show-ref --verify --quiet "refs/remotes/origin/$BranchName"
    if ($LASTEXITCODE -eq 0) {
        throw "Branch already exists on origin: $BranchName"
    }
    if ($LASTEXITCODE -ne 1) {
        throw "Could not inspect origin branch $BranchName."
    }
}

function Invoke-RemoteBaseline($Connection) {
    $safeRepositoryPath = $VmRepositoryPath.Replace("'", "''")
    $safeDryRun = if ($isWhatIf) { '$true' } else { '$false' }
    $result = Invoke-QuailRemotePowerShell $Connection $VmUser $logPath @"
`$repositoryPath = '$safeRepositoryPath'
`$dryRun = $safeDryRun
Set-Location -LiteralPath `$repositoryPath
function Invoke-Git([string[]] `$arguments) {
    `$output = & git @arguments 2>&1
    if (`$LASTEXITCODE -ne 0) { throw "git `$(`$arguments -join ' ') failed with exit code `$LASTEXITCODE." }
    return @(`$output | ForEach-Object { `$_.ToString() })
}
if (@(Invoke-Git @('status', '--porcelain=v1') | Where-Object { `$_ }).Count -ne 0) { throw 'remote-dirty-worktree' }
`$branch = (Invoke-Git @('branch', '--show-current') | Select-Object -Last 1).Trim()
if (`$branch -ne 'main' -and -not `$dryRun) { Invoke-Git @('switch', 'main') | Out-Null }
Invoke-Git @('remote', 'get-url', 'origin') | Out-Null
if (`$dryRun) {
    `$remoteReference = (Invoke-Git @('ls-remote', '--exit-code', 'origin', 'refs/heads/main') | Select-Object -Last 1).Trim().Split([char[]]" `t", [StringSplitOptions]::RemoveEmptyEntries)
    if (`$remoteReference.Count -lt 1) { throw 'Could not read remote main ref.' }
    `$trackedHead = (Invoke-Git @('rev-parse', 'origin/main') | Select-Object -Last 1).Trim()
    if (`$trackedHead -ne `$remoteReference[0]) { throw 'dry-run requires remote origin/main ref current' }
}
else {
    Invoke-Git @('fetch', 'origin') | Out-Null
}
`$relationship = (Invoke-Git @('rev-list', '--left-right', '--count', 'main...origin/main') | Select-Object -Last 1).Trim().Split([char[]]" `t", [StringSplitOptions]::RemoveEmptyEntries)
if (`$relationship.Count -ne 2) { throw 'Could not determine remote main relationship.' }
`$ahead = [int] `$relationship[0]
`$behind = [int] `$relationship[1]
if (`$ahead -gt 0 -and `$behind -gt 0) { throw "remote-main-diverged ahead=`$ahead behind=`$behind" }
if (`$ahead -gt 0) { throw "remote-main-ahead ahead=`$ahead" }
if (`$behind -gt 0) {
    if (`$dryRun) { throw "dry-run requires remote main current; behind=`$behind" }
    Invoke-Git @('merge', '--ff-only', 'origin/main') | Out-Null
}
if (@(Invoke-Git @('status', '--porcelain=v1') | Where-Object { `$_ }).Count -ne 0) { throw 'remote-dirty-worktree-after-baseline' }
`$mainHead = (Invoke-Git @('rev-parse', 'main') | Select-Object -Last 1).Trim()
[pscustomobject]@{ CurrentBranch = (Invoke-Git @('branch', '--show-current') | Select-Object -Last 1).Trim(); MainHead = `$mainHead } | ConvertTo-Json -Compress
"@
    return ($result | Select-Object -Last 1 | ConvertFrom-Json)
}

try {
    Push-Location $repositoryRoot
    try {
        if (-not $CheckpointName) {
            $CheckpointName = "$Milestone-clean"
        }
        if ($CheckpointName -notmatch '^[A-Za-z0-9][A-Za-z0-9._ -]*$') {
            throw 'CheckpointName contains unsupported characters.'
        }

        $dirty = @(Invoke-Git @('status', '--porcelain=v1') | Where-Object { $_ })
        if ($dirty.Count -ne 0) {
            throw 'dirty-worktree'
        }
        Invoke-Git @('remote', 'get-url', 'origin') | Out-Null
        Assert-BranchAvailable

        $hostBranch = (Invoke-Git @('branch', '--show-current') | Select-Object -Last 1).Trim()
        if ($hostBranch -ne 'main') {
            if ($isWhatIf) {
                Write-QuailToolingLog $logPath "Dry run would switch clean host branch $hostBranch to main."
            }
            else {
                Invoke-Git @('switch', 'main') | Out-Null
            }
        }
        if ($isWhatIf) {
            $remoteReference = (Invoke-Git @('ls-remote', '--exit-code', 'origin', 'refs/heads/main') | Select-Object -Last 1).Trim().Split([char[]]" `t", [StringSplitOptions]::RemoveEmptyEntries)
            if ($remoteReference.Count -lt 1) {
                throw 'Could not read remote main ref.'
            }
            $trackedHead = (Invoke-Git @('rev-parse', 'origin/main') | Select-Object -Last 1).Trim()
            if ($trackedHead -ne $remoteReference[0]) {
                throw 'dry-run requires host origin/main ref current'
            }
        }
        else {
            Invoke-Git @('fetch', 'origin') | Out-Null
        }
        $hostRelationship = Get-GitRelationship 'main' 'origin/main'
        if ($hostRelationship.Ahead -gt 0 -and $hostRelationship.Behind -gt 0) {
            throw "host-main-diverged ahead=$($hostRelationship.Ahead) behind=$($hostRelationship.Behind)"
        }
        if ($hostRelationship.Ahead -gt 0) {
            throw "host-main-ahead ahead=$($hostRelationship.Ahead)"
        }
        if ($hostRelationship.Behind -gt 0) {
            if ($isWhatIf) { throw "dry-run requires host main current; behind=$($hostRelationship.Behind)" }
            Invoke-Git @('merge', '--ff-only', 'origin/main') | Out-Null
        }
        if (@(Invoke-Git @('status', '--porcelain=v1') | Where-Object { $_ }).Count -ne 0) {
            throw 'dirty-worktree-after-host-baseline'
        }
        $hostHead = (Invoke-Git @('rev-parse', 'main') | Select-Object -Last 1).Trim()
        $originHead = (Invoke-Git @('rev-parse', 'origin/main') | Select-Object -Last 1).Trim()
        if ($hostHead -ne $originHead) {
            throw 'host-main-not-equal-origin-main'
        }

        $connection = Wait-QuailLabSsh $VmName $VmUser $repositoryRoot $logPath $TimeoutSeconds
        $remote = Invoke-RemoteBaseline $connection
        if (-not $isWhatIf -and $remote.CurrentBranch -ne 'main') {
            throw "remote-not-main actual=$($remote.CurrentBranch)"
        }
        if ($remote.MainHead -ne $hostHead) {
            throw "host-vm-head-mismatch host=$hostHead vm=$($remote.MainHead)"
        }
        $volume = Get-QuailLabDataVolume $connection $VmUser $logPath $DataVolumeLabel

        $existingCheckpoint = @(Get-VMSnapshot -VMName $VmName -ErrorAction Stop | Where-Object { $_.Name -eq $CheckpointName })
        if ($existingCheckpoint.Count -gt 0) {
            throw "checkpoint-already-exists name=$CheckpointName"
        }

        if ($isWhatIf) {
            Write-Output "PASS milestone=$Milestone host=$hostHead vm=$($remote.MainHead) checkpoint=$CheckpointName checkpointStatus=would-create branch=$BranchName branchStatus=would-create vm=true volume=$($volume.Label)"
            exit 0
        }

        Checkpoint-VM -Name $VmName -SnapshotName $CheckpointName -ErrorAction Stop | Out-Null
        Wait-QuailCheckpoint $VmName $CheckpointName $logPath | Out-Null

        Assert-BranchAvailable
        Invoke-Git @('switch', '-c', $BranchName) | Out-Null
        $branchHead = (Invoke-Git @('rev-parse', 'HEAD') | Select-Object -Last 1).Trim()
        if ($branchHead -ne $hostHead) {
            throw 'created-branch-does-not-match-validated-main'
        }

        Write-Output "PASS milestone=$Milestone host=$hostHead vm=$($remote.MainHead) checkpoint=$CheckpointName checkpointStatus=created branch=$BranchName branchStatus=created vm=true volume=$($volume.Label)"
        exit 0
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-QuailToolingLog $logPath $_ | Out-Null
    Fail $_.Exception.Message
}
