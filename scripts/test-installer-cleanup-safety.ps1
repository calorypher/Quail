[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerScriptPath = Join-Path $repositoryRoot 'packaging\Quail.iss'
$installerScript = Get-Content -Raw -LiteralPath $installerScriptPath

function Assert-DoesNotMatch([string] $Pattern, [string] $Message) {
    if ($installerScript -match $Pattern) {
        throw $Message
    }
}

Assert-DoesNotMatch '(?im)^\s*\[InstallDelete\]\s*$' 'Packaging must not define installer cleanup actions.'
Assert-DoesNotMatch '(?im)^\s*Type:\s*filesandordirs\s*;' 'Packaging must not use recursive filesandordirs cleanup.'
Assert-DoesNotMatch '(?im)\bDelTree\s*\(' 'Packaging must not call DelTree for installer cleanup.'
Assert-DoesNotMatch '(?im)\bRemove-Item\b.*\{app\}.*\b-Recurse\b' 'Packaging must not shell out to recursive {app} cleanup.'
Assert-DoesNotMatch '(?im)\b(rmdir|rd)\b.*\{app\}' 'Packaging must not shell out to directory-tree cleanup.'

if (Test-Path -LiteralPath (Join-Path $repositoryRoot 'packaging\LegacySelfContained0_1.issinc')) {
    throw 'The removed legacy 0.1 cleanup include must not be present.'
}

foreach ($requiredDirective in @(
    'DefaultDirName={#AppDirectory}',
    'DisableDirPage=yes',
    'UsePreviousAppDir=no')) {
    if ($installerScript -notlike "*$requiredDirective*") {
        throw "Fixed-location directive is missing: $requiredDirective"
    }
}

foreach ($forbiddenToken in @(
    'LegacySelfContained0_1.issinc',
    'IsRecognizedQuailInstallation',
    'ValidateDestinationOwnership',
    'NextButtonClick')) {
    if ($installerScript -like "*$forbiddenToken*") {
        throw "Obsolete custom-directory compatibility token remains: $forbiddenToken"
    }
}

foreach ($requiredToken in @(
    'function CanonicalInstallationDirectory',
    "ExpandConstant('{autopf}\Quail')",
    'function ValidateFixedInstallationContract',
    'WizardDirValue',
    "ExpandConstant('{app}')",
    "'DisplayVersion'",
    "'InstallLocation'",
    "'UninstallString'",
    "Result := ValidateFixedInstallationContract;",
    'TargetPath := CanonicalInstallationDirectory;')) {
    if ($installerScript -notlike "*$requiredToken*") {
        throw "Fixed-location contract token is missing: $requiredToken"
    }
}

if ($installerScript -match "TargetPath := ExpandConstant\('\{app\}'\);") {
    throw 'PATH mutation must not derive its entry from the selected application directory.'
}

if ($installerScript -notmatch '(?s)function PrepareToInstall.*?Result := ValidateFixedInstallationContract;.*?if not HasDesktopRuntime') {
    throw 'Fixed-location validation must run before prerequisite work.'
}

Write-Output 'PASS installer fixed-location safety'
