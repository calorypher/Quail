Set-StrictMode -Version Latest

function New-QuailToolingLog {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Prefix
    )

    $logDirectory = Join-Path $RepositoryRoot '.quail-tooling'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    return Join-Path $logDirectory ("{0}-{1:yyyyMMdd-HHmmssfff}.log" -f $Prefix, (Get-Date))
}

function Write-QuailToolingLog {
    param(
        [Parameter(Mandatory)] [string] $LogPath,
        [Parameter(Mandatory)] [string] $Message
    )

    Add-Content -LiteralPath $LogPath -Value $Message
}

function Invoke-QuailLoggedExternal {
    param(
        [Parameter(Mandatory)] [string] $LogPath,
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    Write-QuailToolingLog $LogPath ("> $FilePath $($Arguments -join ' ')")
    $output = & $FilePath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object {
        $line = $_.ToString()
        if ($line.Length -gt 0) {
            Write-QuailToolingLog $LogPath $line
        }
    }
    if ($exitCode -ne 0) {
        $lastOutput = @($output | ForEach-Object { $_.ToString() } | Where-Object { $_.Trim() } | Select-Object -Last 1)
        $detail = if ($lastOutput.Count -gt 0) { ": $($lastOutput[0].Trim())" } else { '' }
        throw "$FilePath failed with exit code $exitCode$detail"
    }

    return @($output | ForEach-Object { $_.ToString() })
}

function ConvertTo-QuailEncodedPowerShellCommand {
    param([Parameter(Mandatory)][string] $Script)

    return [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script))
}

function Get-QuailSshOptions {
    param(
        [Parameter(Mandatory)][string] $VmName,
        [Parameter(Mandatory)][string] $KnownHostsPath,
        [Parameter(Mandatory)][int] $ConnectTimeoutSeconds
    )

    # HostKeyAlias pins trust to the lab identity rather than its DHCP address.
    # accept-new accepts only the first key; a later mismatch is rejected by OpenSSH.
    return @(
        '-o', 'BatchMode=yes',
        '-o', "ConnectTimeout=$ConnectTimeoutSeconds",
        '-o', "HostKeyAlias=$VmName",
        '-o', "UserKnownHostsFile=$KnownHostsPath",
        '-o', 'StrictHostKeyChecking=accept-new'
    )
}

function Get-QuailToolingKnownHostsPath {
    param([Parameter(Mandatory)][string] $RepositoryRoot)

    return Join-Path $RepositoryRoot '.quail-tooling\quail-lab-known_hosts'
}

function Get-QuailUsableIPv4Addresses {
    param([Parameter(Mandatory)][string] $VmName)

    return @(
        Get-VMNetworkAdapter -VMName $VmName -ErrorAction Stop |
            Select-Object -ExpandProperty IPAddresses |
            Where-Object {
                $_ -match '^\d{1,3}(\.\d{1,3}){3}$' -and
                $_ -notmatch '^169\.254\.' -and
                $_ -notmatch '^0\.'
            } |
            Sort-Object -Unique
    )
}

function Wait-QuailLabSsh {
    param(
        [Parameter(Mandatory)][string] $VmName,
        [Parameter(Mandatory)][string] $VmUser,
        [Parameter(Mandatory)][string] $RepositoryRoot,
        [Parameter(Mandatory)][string] $LogPath,
        [ValidateRange(1, 600)][int] $TimeoutSeconds = 90,
        [ValidateRange(1, 60)][int] $ConnectTimeoutSeconds = 10
    )

    $vm = Get-VM -Name $VmName -ErrorAction Stop
    $state = $vm.State.ToString()
    switch ($state) {
        'Running' { Write-QuailToolingLog $LogPath "VM $VmName is already Running." }
        'Off' {
            Write-QuailToolingLog $LogPath "Starting Off VM $VmName."
            Start-VM -Name $VmName -ErrorAction Stop | Out-Null
        }
        default { throw "VM $VmName is in unsafe state $state." }
    }

    $knownHostsPath = Get-QuailToolingKnownHostsPath $RepositoryRoot
    $sshOptions = Get-QuailSshOptions $VmName $knownHostsPath $ConnectTimeoutSeconds
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $addresses = @(Get-QuailUsableIPv4Addresses $VmName)
        foreach ($address in $addresses) {
            try {
                Invoke-QuailLoggedExternal $LogPath 'ssh' @($sshOptions + @("$VmUser@$address", 'exit')) | Out-Null
                return [pscustomobject]@{
                    VmName = $VmName
                    IpAddress = $address
                    KnownHostsPath = $knownHostsPath
                    SshOptions = $sshOptions
                }
            }
            catch {
                Write-QuailToolingLog $LogPath "SSH probe failed for ${address}: $($_.Exception.Message)"
            }
        }

        if ((Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)

    throw "VM SSH readiness timed out after $TimeoutSeconds seconds."
}

function Invoke-QuailRemotePowerShell {
    param(
        [Parameter(Mandatory)] $Connection,
        [Parameter(Mandatory)][string] $VmUser,
        [Parameter(Mandatory)][string] $LogPath,
        [Parameter(Mandatory)][string] $Script
    )

    $encodedCommand = ConvertTo-QuailEncodedPowerShellCommand $Script
    return Invoke-QuailLoggedExternal $LogPath 'ssh' @(
        $Connection.SshOptions + @(
            "$VmUser@$($Connection.IpAddress)",
            "powershell -NoProfile -NonInteractive -EncodedCommand $encodedCommand"
        )
    )
}

function Get-QuailLabDataVolume {
    param(
        [Parameter(Mandatory)] $Connection,
        [Parameter(Mandatory)][string] $VmUser,
        [Parameter(Mandatory)][string] $LogPath,
        [Parameter(Mandatory)][string] $DataVolumeLabel
    )

    $safeLabel = $DataVolumeLabel.Replace("'", "''")
    $result = Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath @"
`$volume = @(Get-Volume | Where-Object { `$_.FileSystemLabel -eq '$safeLabel' })
if (`$volume.Count -ne 1) { throw 'Expected exactly one data volume with the requested label.' }
`$selected = `$volume[0]
if (`$selected.FileSystem -ne 'NTFS') { throw 'The labeled data volume is not NTFS.' }
if (`$selected.HealthStatus -ne 'Healthy') { throw 'The labeled data volume is not healthy.' }
[pscustomobject]@{ Label = `$selected.FileSystemLabel; DriveLetter = `$selected.DriveLetter; FileSystem = `$selected.FileSystem; Health = `$selected.HealthStatus } | ConvertTo-Json -Compress
"@
    return ($result | Select-Object -Last 1 | ConvertFrom-Json)
}

function Wait-QuailCheckpoint {
    param(
        [Parameter(Mandatory)][string] $VmName,
        [Parameter(Mandatory)][string] $CheckpointName,
        [Parameter(Mandatory)][string] $LogPath,
        [ValidateRange(1, 60)][int] $TimeoutSeconds = 15,
        [ValidateRange(1, 5000)][int] $PollIntervalMilliseconds = 250,
        [scriptblock] $SnapshotReader = {
            param([string] $Name)
            return @(Get-VMSnapshot -VMName $Name -ErrorAction Stop)
        },
        [scriptblock] $NowProvider = { Get-Date },
        [scriptblock] $SleepAction = {
            param([int] $Milliseconds)
            Start-Sleep -Milliseconds $Milliseconds
        }
    )

    $deadline = (& $NowProvider).AddSeconds($TimeoutSeconds)
    $attempt = 0
    $lastCount = 0

    do {
        $attempt++
        $matches = @(& $SnapshotReader $VmName | Where-Object { $_.Name -eq $CheckpointName })
        $lastCount = $matches.Count
        if ($lastCount -eq 1) {
            Write-QuailToolingLog $LogPath "Checkpoint verification succeeded name=$CheckpointName attempts=$attempt count=1."
            return $matches[0]
        }

        if ($lastCount -gt 1) {
            Write-QuailToolingLog $LogPath "Checkpoint verification failed name=$CheckpointName attempts=$attempt count=$lastCount."
            throw "checkpoint-create-verification-failed name=$CheckpointName attempts=$attempt count=$lastCount"
        }

        Write-QuailToolingLog $LogPath "Checkpoint verification pending name=$CheckpointName attempts=$attempt count=$lastCount."
        if ((& $NowProvider) -lt $deadline) {
            & $SleepAction $PollIntervalMilliseconds
        }
    } while ((& $NowProvider) -lt $deadline)

    throw "checkpoint-create-verification-timed-out name=$CheckpointName attempts=$attempt count=$lastCount"
}

function Get-QuailSha256Text {
    param([Parameter(Mandatory)][string] $Text)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
        return ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
    }
}

Export-ModuleMember -Function @(
    'New-QuailToolingLog',
    'Write-QuailToolingLog',
    'Invoke-QuailLoggedExternal',
    'Wait-QuailLabSsh',
    'Invoke-QuailRemotePowerShell',
    'Get-QuailLabDataVolume',
    'Wait-QuailCheckpoint',
    'Get-QuailSha256Text'
)
