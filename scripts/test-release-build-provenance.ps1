[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory)]
    [string] $PhysicalCheckoutRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-BytesContainText {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes,

        [Parameter(Mandatory)]
        [string] $Value
    )

    foreach ($encoding in @([Text.Encoding]::UTF8, [Text.Encoding]::Unicode, [Text.Encoding]::BigEndianUnicode)) {
        if ($encoding.GetString($Bytes).IndexOf($Value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

if (-not (Test-Path -LiteralPath $PayloadDirectory -PathType Container)) {
    throw "Payload directory does not exist: $PayloadDirectory"
}

if (-not (Test-Path -LiteralPath $PhysicalCheckoutRoot -PathType Container)) {
    throw "Physical checkout root does not exist: $PhysicalCheckoutRoot"
}

$forbiddenValues = [ordered]@{
    physicalCheckoutRoot = [IO.Path]::GetFullPath($PhysicalCheckoutRoot).TrimEnd([char]92, [char]47)
}

foreach ($profileRoot in @($env:USERPROFILE, [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique) {
    $forbiddenValues["userProfileRoot$($forbiddenValues.Count)"] = [IO.Path]::GetFullPath($profileRoot).TrimEnd([char]92, [char]47)
}

$matches = @{}
foreach ($file in Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File) {
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    foreach ($forbidden in $forbiddenValues.GetEnumerator()) {
        if (Test-BytesContainText -Bytes $bytes -Value $forbidden.Value) {
            if (-not $matches.ContainsKey($forbidden.Key)) {
                $matches[$forbidden.Key] = @()
            }

            $matches[$forbidden.Key] += [IO.Path]::GetRelativePath($PayloadDirectory, $file.FullName)
        }
    }
}

if ($matches.Count -gt 0) {
    $classes = $matches.Keys | Sort-Object
    throw "Release payload contains forbidden physical build provenance classes: $($classes -join ', ')."
}

Write-Output "PASS release build provenance payloadFiles=$(@(Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File).Count) forbiddenClasses=$($forbiddenValues.Count)"
