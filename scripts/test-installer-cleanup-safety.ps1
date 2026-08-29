[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerScriptPath = Join-Path $repositoryRoot 'packaging\Quail.iss'
$legacyCleanupPath = Join-Path $repositoryRoot 'packaging\LegacySelfContained0_1.issinc'
$installerScript = Get-Content -Raw -LiteralPath $installerScriptPath
$legacyCleanup = Get-Content -LiteralPath $legacyCleanupPath
$combinedPackaging = $installerScript + "`n" + ($legacyCleanup -join "`n")

function Assert-DoesNotMatch([string] $Pattern, [string] $Message) {
    if ($combinedPackaging -match $Pattern) {
        throw $Message
    }
}

Assert-DoesNotMatch '(?im)^\s*Type:\s*filesandordirs\s*;' 'Packaging must not use recursive filesandordirs cleanup.'
Assert-DoesNotMatch '(?im)\bDelTree\s*\(' 'Packaging must not call DelTree for installer cleanup.'
Assert-DoesNotMatch '(?im)\bRemove-Item\b.*\{app\}.*\b-Recurse\b' 'Packaging must not shell out to recursive {app} cleanup.'
Assert-DoesNotMatch '(?im)\b(rmdir|rd)\b.*\{app\}' 'Packaging must not shell out to directory-tree cleanup.'

if ($installerScript -notmatch '(?im)^\s*#include\s+"LegacySelfContained0_1\.issinc"\s*$') {
    throw 'Quail.iss must include the exact 0.1 legacy cleanup list.'
}

$legacyEntries = @($legacyCleanup | Where-Object { $_ -match '^Type:' })
if ($legacyEntries.Count -ne 187) {
    throw "Expected 187 exact legacy cleanup entries, found $($legacyEntries.Count)."
}

foreach ($entry in $legacyEntries) {
    if ($entry -notmatch '^Type: files; Name: "\{app\}\\[^*?]+"; Check: IsRecognizedQuailInstallation$') {
        throw "Legacy cleanup entry is not a guarded exact file path: $entry"
    }
}

foreach ($requiredToken in @(
    'function IsRecognizedQuailInstallation',
    "'InstallLocation'",
    "'UninstallString'",
    'function ValidateDestinationOwnership',
    'Result := ValidateDestinationOwnership;',
    'function NextButtonClick')) {
    if ($installerScript -notlike "*$requiredToken*") {
        throw "Ownership validation token is missing: $requiredToken"
    }
}

if ($installerScript -notmatch '(?s)function NextButtonClick.*?ValidationError := ValidateDestinationOwnership;') {
    throw 'Interactive directory selection must invoke ownership validation.'
}

Write-Output "PASS installer cleanup safety legacyEntries=$($legacyEntries.Count)"
