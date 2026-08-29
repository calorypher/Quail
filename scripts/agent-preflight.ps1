[CmdletBinding()]
param(
    [string] $ExpectedBranch,
    [switch] $RequireClean,
    [switch] $RequireCurrentOrigin,
    [switch] $CheckTools
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$logDirectory = Join-Path $repositoryRoot '.quail-tooling'
New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$logPath = Join-Path $logDirectory ("agent-preflight-{0:yyyyMMdd-HHmmssfff}.log" -f (Get-Date))

function Write-Log([string] $Message) {
    Add-Content -LiteralPath $logPath -Value $Message
}

function Invoke-Git([string[]] $Arguments) {
    $output = & git @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $output | ForEach-Object { Write-Log $_.ToString() }
    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode."
    }

    return @($output | ForEach-Object { $_.ToString() })
}

function Fail([string] $Reason) {
    $compactReason = ($Reason -replace '[\r\n]+', ' ').Trim()
    Write-Output "FAIL reason=$compactReason log=$logPath"
    exit 1
}

try {
    Push-Location $repositoryRoot
    try {
        $branch = (Invoke-Git @('branch', '--show-current') | Select-Object -Last 1).Trim()
        $head = (Invoke-Git @('rev-parse', 'HEAD') | Select-Object -Last 1).Trim()
        $dirtyEntries = @(Invoke-Git @('status', '--porcelain=v1') | Where-Object { $_ })
        $isClean = $dirtyEntries.Count -eq 0

        if ($RequireClean -and -not $isClean) {
            Write-Log "dirty entries: $($dirtyEntries -join '; ')"
            Fail 'dirty-worktree'
        }

        if ($ExpectedBranch -and $branch -ne $ExpectedBranch) {
            Fail "unexpected-branch expected=$ExpectedBranch actual=$branch"
        }

        Invoke-Git @('remote', 'get-url', 'origin') | Out-Null
        $current = 'unchecked'
        if ($RequireCurrentOrigin) {
            if (-not $isClean) {
                Fail 'dirty-worktree'
            }

            Invoke-Git @('fetch', 'origin') | Out-Null
            $relationship = (Invoke-Git @('rev-list', '--left-right', '--count', 'HEAD...origin/main') | Select-Object -Last 1).Trim().Split([char[]]" `t", [System.StringSplitOptions]::RemoveEmptyEntries)
            if ($relationship.Count -ne 2) {
                throw 'Could not determine the relationship to origin/main.'
            }

            $ahead = [int] $relationship[0]
            $behind = [int] $relationship[1]
            if ($behind -eq 0) {
                $current = 'true'
            }
            elseif ($ahead -gt 0 -and $behind -gt 0) {
                Fail "diverged ahead=$ahead behind=$behind"
            }
            else {
                Fail "not-current ahead=$ahead behind=$behind"
            }
        }

        $tools = 'unchecked'
        if ($CheckTools) {
            $requiredCommands = @('git', 'dotnet', 'ssh', 'scp', 'powershell', 'Get-VMNetworkAdapter')
            $missingCommands = @($requiredCommands | Where-Object { -not (Get-Command $_ -ErrorAction SilentlyContinue) })
            if ($missingCommands.Count -gt 0) {
                Fail "missing-tools=$($missingCommands -join ',')"
            }

            $tools = 'ok'
        }

        Write-Output "PASS branch=$branch head=$head clean=$($isClean.ToString().ToLowerInvariant()) current=$current tools=$tools origin=configured"
        exit 0
    }
    finally {
        Pop-Location
    }
}
catch {
    Write-Log $_ | Out-Null
    Fail $_.Exception.Message
}
