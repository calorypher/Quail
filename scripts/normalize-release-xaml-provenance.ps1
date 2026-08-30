[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $GeneratedSourceDirectory,

    [Parameter(Mandatory)]
    [string] $PhysicalCheckoutRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $GeneratedSourceDirectory -PathType Container)) {
    throw "Generated XAML source directory does not exist: $GeneratedSourceDirectory"
}

if (-not (Test-Path -LiteralPath $PhysicalCheckoutRoot -PathType Container)) {
    throw "Physical checkout root does not exist: $PhysicalCheckoutRoot"
}

$root = [IO.Path]::GetFullPath($PhysicalCheckoutRoot).TrimEnd([char]92, [char]47)
$pattern = '(?m)^(#pragma checksum ")' + [Regex]::Escape($root) + '([\\/][^"]+")'
$generatedFiles = @(Get-ChildItem -LiteralPath $GeneratedSourceDirectory -Filter '*.cs' -File | Where-Object { $_.Name -match '\.g(\.i)?\.cs$' })
$normalizedDirectives = 0

foreach ($file in $generatedFiles) {
    $content = Get-Content -LiteralPath $file.FullName -Raw
    if ($null -eq $content) {
        continue
    }

    $updated = [Regex]::Replace(
        $content,
        $pattern,
        {
            param($match)
            $script:normalizedDirectives++
            $relativePath = $match.Groups[2].Value.TrimStart([char]92, [char]47).Replace('\', '/')
            return $match.Groups[1].Value + '/_/' + $relativePath
        })

    if ($updated -ne $content) {
        [IO.File]::WriteAllText($file.FullName, $updated, [Text.UTF8Encoding]::new($false))
    }
}

Write-Output "PASS Release XAML provenance generatedFiles=$($generatedFiles.Count) normalizedChecksumDirectives=$normalizedDirectives"
