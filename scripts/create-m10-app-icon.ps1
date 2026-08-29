[CmdletBinding()]
param(
    [string] $SourceDirectory = 'G:\Mój dysk\Moje notatki\Projekty\AI i Codex\Quail\Branding\Logo A - Flow\App Icon\Selected',
    [string] $SmallIconDirectory = 'G:\Mój dysk\Moje notatki\Projekty\AI i Codex\Quail\Branding\Logo A - Flow\Small Icons',
    [string] $Output = 'src\Quail.App\Assets\quail-app-icon.ico'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repositoryRoot $Output
$frames = @(
    @{ Size = 16; Name = 'quail-feather-A-small-16px.png'; Source = $SmallIconDirectory; Hash = 'E19CE347F912941CC8118098AF9458712C65483B7E1F39AC1006911162838242' },
    @{ Size = 32; Name = 'quail-app-icon-transparent-32px.png'; Source = $SourceDirectory; Hash = '015DA35E33C734B93BFA15B47DF1D9BA93E7DF0369427410908489F9D25A6637' },
    @{ Size = 48; Name = 'quail-app-icon-transparent-48px.png'; Source = $SourceDirectory; Hash = 'A889C25C22D80EE8B5AAFBF6E6E3FFBD8D5FE8091B52F965B2AC9EFFD45F3C9B' },
    @{ Size = 64; Name = 'quail-app-icon-transparent-64px.png'; Source = $SourceDirectory; Hash = '25B1D79A758A2AFF5ABEAF671F2A8FCF1C66E290BC7351B290BF0F699045F9E5' },
    @{ Size = 128; Name = 'quail-app-icon-transparent-128px.png'; Source = $SourceDirectory; Hash = '214D80584D034D9B9647E0E670793C9EF42DDED42A3EA0EFE5E349485DFA32B8' },
    @{ Size = 256; Name = 'quail-app-icon-transparent-256px.png'; Source = $SourceDirectory; Hash = 'B08E1DC37E3B42D06C835E733A0FE5A2F6143B33F580FB5D34A53E2EDEE18A4D' }
)

foreach ($frame in $frames) {
    $frame.Path = Join-Path $frame.Source $frame.Name
    if (-not (Test-Path -LiteralPath $frame.Path -PathType Leaf)) {
        throw "Missing approved PNG frame: $($frame.Path)"
    }

    $actualHash = (Get-FileHash -LiteralPath $frame.Path -Algorithm SHA256).Hash
    if ($actualHash -ne $frame.Hash) {
        throw "Approved PNG hash mismatch for $($frame.Name): expected $($frame.Hash), got $actualHash"
    }

    $frame.Bytes = [System.IO.File]::ReadAllBytes($frame.Path)
    if ($frame.Bytes.Length -lt 8 -or
        $frame.Bytes[0] -ne 137 -or
        $frame.Bytes[1] -ne 80 -or
        $frame.Bytes[2] -ne 78 -or
        $frame.Bytes[3] -ne 71 -or
        $frame.Bytes[4] -ne 13 -or
        $frame.Bytes[5] -ne 10 -or
        $frame.Bytes[6] -ne 26 -or
        $frame.Bytes[7] -ne 10) {
        throw "Expected a PNG frame: $($frame.Path)"
    }
}

$outputDirectory = Split-Path -Parent $outputPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$stream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
try {
    $writer = [System.IO.BinaryWriter]::new($stream)
    try {
        # ICONDIR header: reserved, type=icon, frame count.
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { [byte]0 } else { [byte]$frame.Size }
            $writer.Write($dimension)
            $writer.Write($dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$offset)
            $offset += $frame.Bytes.Length
        }

        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    $stream.Dispose()
}

Write-Output "PASS output=$outputPath sha256=$((Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash) frames=$($frames.Size -join ',')"
