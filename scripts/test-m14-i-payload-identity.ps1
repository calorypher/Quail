[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PayloadDirectory,

    [Parameter(Mandatory)]
    [string] $ReferenceManifestPath
)

$ErrorActionPreference = 'Stop'

function Get-NormalizedRelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    return $Path.Replace('/', '\\').TrimStart('\\')
}

if (-not (Test-Path -LiteralPath $PayloadDirectory -PathType Container)) {
    throw "Payload directory does not exist: $PayloadDirectory"
}

if (-not (Test-Path -LiteralPath $ReferenceManifestPath -PathType Leaf)) {
    throw "Reference manifest does not exist: $ReferenceManifestPath"
}

$reference = Get-Content -LiteralPath $ReferenceManifestPath -Raw | ConvertFrom-Json
if ($reference.FileCount -ne 56 -or $reference.Bytes -ne 43927601) {
    throw 'Reference manifest does not describe the approved M13-D payload identity.'
}

$actualFiles = @(Get-ChildItem -LiteralPath $PayloadDirectory -Recurse -File | ForEach-Object {
        $relativePath = Get-NormalizedRelativePath ([System.IO.Path]::GetRelativePath($PayloadDirectory, $_.FullName))
        [pscustomobject]@{
            Path = $relativePath
            Length = $_.Length
            Sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })

$actualBytes = ($actualFiles | Measure-Object -Property Length -Sum).Sum
if ($actualFiles.Count -ne $reference.FileCount -or $actualBytes -ne $reference.Bytes) {
    throw "M13-D payload cardinality mismatch: actual files=$($actualFiles.Count), bytes=$actualBytes."
}

$expectedByPath = @{}
foreach ($file in $reference.Files) {
    $expectedByPath[(Get-NormalizedRelativePath $file.Path)] = $file
}

$actualByPath = @{}
foreach ($file in $actualFiles) {
    $actualByPath[$file.Path] = $file
}

$differences = @()
foreach ($path in $expectedByPath.Keys) {
    if (-not $actualByPath.ContainsKey($path)) {
        $differences += "missing:$path"
        continue
    }

    $expected = $expectedByPath[$path]
    $actual = $actualByPath[$path]
    if ($actual.Length -ne $expected.Length -or $actual.Sha256 -ne $expected.Sha256.ToLowerInvariant()) {
        $differences += "changed:$path"
    }
}

foreach ($path in $actualByPath.Keys) {
    if (-not $expectedByPath.ContainsKey($path)) {
        $differences += "unexpected:$path"
    }
}

if ($differences.Count -gt 0) {
    throw "M13-D payload byte identity failed: $($differences -join ', ')."
}

Write-Output "PASS M13-D payload byte identity files=$($actualFiles.Count) bytes=$actualBytes Quail.exe=$($actualByPath['Quail.exe'].Sha256) Quail.Cli.exe=$($actualByPath['Quail.Cli.exe'].Sha256)"
