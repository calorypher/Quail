[CmdletBinding()]
param(
    [string] $PinsPath,
    [string] $OutputDirectory = '.quail-tooling\installer-prerequisites'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $PinsPath) { $PinsPath = Join-Path $repositoryRoot 'packaging\prerequisite-pins.json' }
$pinsPath = (Resolve-Path -LiteralPath $PinsPath -ErrorAction Stop).Path
$cacheDirectory = Join-Path $repositoryRoot $OutputDirectory
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null

function Get-Pin($Pins, [string] $Id) {
    $entry = @($Pins | Where-Object { $_.id -eq $Id })
    if ($entry.Count -ne 1) { throw "Canonical prerequisite pins must contain exactly one $Id entry." }
    return $entry[0]
}

function Assert-Pin([object] $Pin) {
    foreach ($name in @('id', 'fileName', 'source', 'sha256')) {
        if ([string]::IsNullOrWhiteSpace([string]$Pin.$name)) { throw "Canonical prerequisite pin is missing $name." }
    }

    $uri = [uri]$Pin.source
    $approvedHosts = @('builds.dotnet.microsoft.com', 'download.microsoft.com', 'download.visualstudio.microsoft.com')
    if ($uri.Scheme -ne 'https' -or $approvedHosts -notcontains $uri.Host.ToLowerInvariant()) {
        throw "Canonical prerequisite pin is not an approved immutable Microsoft HTTPS source: $($Pin.source)"
    }

    if ($Pin.sha256 -notmatch '^[0-9a-fA-F]{64}$') { throw "Canonical prerequisite pin has an invalid SHA-256: $($Pin.id)" }
}

function Get-FileSha256([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Acquire-PinnedPrerequisite([object] $Pin) {
    $path = Join-Path $cacheDirectory $Pin.fileName
    if (Test-Path -LiteralPath $path -PathType Leaf) {
        if ((Get-FileSha256 $path) -ne $Pin.sha256.ToLowerInvariant()) {
            throw "Cached prerequisite does not match its canonical SHA-256 pin: $path"
        }
    }
    else {
        $downloadPath = "$path.download"
        if (Test-Path -LiteralPath $downloadPath -PathType Leaf) { throw "Interrupted prerequisite download remains: $downloadPath" }
        Invoke-WebRequest -Uri $Pin.source -OutFile $downloadPath
        if ((Get-FileSha256 $downloadPath) -ne $Pin.sha256.ToLowerInvariant()) {
            Remove-Item -LiteralPath $downloadPath -Force
            throw "Downloaded prerequisite does not match its canonical SHA-256 pin: $($Pin.id)"
        }
        Move-Item -LiteralPath $downloadPath -Destination $path
    }

    $item = Get-Item -LiteralPath $path
    $entry = [ordered]@{
        id = $Pin.id
        fileName = $Pin.fileName
        source = $Pin.source
        resolvedPath = $item.FullName
        sizeBytes = $item.Length
        sha256 = $Pin.sha256.ToLowerInvariant()
    }
    if ($Pin.PSObject.Properties.Name -contains 'requiredVersion') { $entry.requiredVersion = $Pin.requiredVersion }
    if ($Pin.PSObject.Properties.Name -contains 'minimumVersion') { $entry.minimumVersion = $Pin.minimumVersion }
    if ($Pin.PSObject.Properties.Name -contains 'channel') { $entry.channel = $Pin.channel }
    return [pscustomobject]$entry
}

$pinDocument = Get-Content -Raw -LiteralPath $pinsPath | ConvertFrom-Json
if ($pinDocument.schemaVersion -ne 1 -or $null -eq $pinDocument.prerequisites) { throw 'Unsupported canonical prerequisite pin manifest.' }
$requiredIds = @('dotnet-desktop-runtime-x64', 'windows-app-runtime-x64', 'vc-redist-x64')
$pins = foreach ($id in $requiredIds) {
    $pin = Get-Pin $pinDocument.prerequisites $id
    Assert-Pin $pin
    $pin
}

$resolved = @($pins | ForEach-Object { Acquire-PinnedPrerequisite $_ })
$manifestPath = Join-Path $cacheDirectory 'prerequisites.json'
$resolved | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "PASS manifest=$manifestPath prerequisites=$($resolved.Count) pins=$pinsPath"
