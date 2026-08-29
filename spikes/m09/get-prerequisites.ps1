[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$cacheDirectory = Join-Path $repositoryRoot '.quail-tooling\m09-prerequisites'
New-Item -ItemType Directory -Path $cacheDirectory -Force | Out-Null

function Resolve-OfficialSource([string] $Url, [string[]] $ApprovedHosts) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $true
    $client = [System.Net.Http.HttpClient]::new($handler)

    try {
        $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, $Url)
        $response = $client.SendAsync(
            $request,
            [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()

        if (-not $response.IsSuccessStatusCode) {
            throw "Prerequisite source returned HTTP $([int]$response.StatusCode): $Url"
        }

        $resolved = $response.RequestMessage.RequestUri
        if ($resolved.Scheme -ne 'https' -or $ApprovedHosts -notcontains $resolved.Host.ToLowerInvariant()) {
            throw "Prerequisite source resolved outside the approved Microsoft HTTPS hosts: $($resolved.AbsoluteUri)"
        }

        return $resolved.AbsoluteUri
    }
    finally {
        if ($response) { $response.Dispose() }
        $client.Dispose()
        $handler.Dispose()
    }
}

function Get-ExecutableVersion([string] $Path) {
    $fileVersion = (Get-Item -LiteralPath $Path).VersionInfo.FileVersion
    $match = [regex]::Match($fileVersion, '\d+(?:\.\d+){1,3}')
    if (-not $match.Success) {
        throw "Could not derive a numeric executable version from ${Path}: $fileVersion"
    }

    return ([version]$match.Value).ToString()
}

$prerequisites = @(
    [pscustomobject]@{
        Id = 'dotnet-desktop-runtime-x64'
        FileName = 'windowsdesktop-runtime-10.0.11-win-x64.exe'
        Url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.11/windowsdesktop-runtime-10.0.11-win-x64.exe'
        ApprovedHosts = @('builds.dotnet.microsoft.com')
        RequiredVersion = '10.0.11'
    },
    [pscustomobject]@{
        Id = 'windows-app-runtime-x64'
        FileName = 'WindowsAppRuntimeInstall-x64.exe'
        Url = 'https://aka.ms/windowsappsdk/2.4/2.4.0/windowsappruntimeinstall-x64.exe'
        ApprovedHosts = @('download.microsoft.com')
        RequiredVersion = '2.4.0.0'
    },
    [pscustomobject]@{
        Id = 'vc-redist-x64'
        FileName = 'VC_redist.x64.exe'
        Url = 'https://aka.ms/vc14/vc_redist.x64.exe'
        ApprovedHosts = @('download.visualstudio.microsoft.com')
    }
)

$resolved = foreach ($prerequisite in $prerequisites) {
    $path = Join-Path $cacheDirectory $prerequisite.FileName
    $resolvedSource = Resolve-OfficialSource $prerequisite.Url $prerequisite.ApprovedHosts
    $downloadPath = "$path.download"
    if (Test-Path -LiteralPath $downloadPath -PathType Leaf) {
        Remove-Item -LiteralPath $downloadPath -Force
    }
    Invoke-WebRequest -Uri $resolvedSource -OutFile $downloadPath
    Move-Item -LiteralPath $downloadPath -Destination $path -Force

    $item = Get-Item -LiteralPath $path
    $entry = [ordered]@{
        id = $prerequisite.Id
        fileName = $prerequisite.FileName
        originalSource = $prerequisite.Url
        source = $resolvedSource
        resolvedPath = $item.FullName
        sizeBytes = $item.Length
        sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    }

    if (($prerequisite.PSObject.Properties.Name -contains 'RequiredVersion') -and $prerequisite.RequiredVersion) {
        $entry.requiredVersion = $prerequisite.RequiredVersion
    }
    if ($prerequisite.Id -eq 'vc-redist-x64') {
        $entry.minimumVersion = Get-ExecutableVersion $path
    }

    [pscustomobject]$entry
}

$manifestPath = Join-Path $cacheDirectory 'prerequisites.json'
$resolved | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "PASS manifest=$manifestPath prerequisites=$($resolved.Count)"
