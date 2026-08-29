[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot '..\packaging\WindowsAppRuntimeDetection.psm1') -Force

function New-Package([string] $Name, [string] $Version, [bool] $RegisteredForCurrentUser = $true) {
    [pscustomobject]@{
        Name = $Name
        Version = $Version
        Architecture = 'X64'
        Status = 'Ok'
        IsDevelopmentMode = $false
        RegisteredForCurrentUser = $RegisteredForCurrentUser
    }
}

function New-StableRuntime([string] $ReleaseVersion = '2.4.0.0', [bool] $RegisteredForCurrentUser = $true) {
    @(
        (New-Package 'Microsoft.WindowsAppRuntime.2' $ReleaseVersion $RegisteredForCurrentUser),
        (New-Package 'MicrosoftCorporationII.WinAppRuntime.Main.2' $ReleaseVersion $RegisteredForCurrentUser),
        (New-Package 'MicrosoftCorporationII.WinAppRuntime.Singleton' "800$ReleaseVersion" $RegisteredForCurrentUser),
        (New-Package "Microsoft.WinAppRuntime.DDLM.$ReleaseVersion-x6" $ReleaseVersion $RegisteredForCurrentUser)
    )
}

$minimum = [version]'2.4.0.0'
$cases = @(
    [pscustomobject]@{ Name = 'required stable 2.4.0 present'; Packages = New-StableRuntime; Expected = $true },
    [pscustomobject]@{ Name = 'compatible newer stable 2.5.1 present'; Packages = New-StableRuntime '2.5.1.0'; Expected = $true },
    [pscustomobject]@{ Name = 'older DDLM is rejected'; Packages = @((New-StableRuntime)[0..2] + (New-Package 'Microsoft.WinAppRuntime.DDLM.2.3.9.0-x6' '2.3.9.0')); Expected = $false },
    [pscustomobject]@{ Name = 'missing DDLM is rejected'; Packages = @((New-StableRuntime)[0..2]); Expected = $false },
    [pscustomobject]@{ Name = 'other-user registrations are rejected'; Packages = New-StableRuntime '2.4.0.0' $false; Expected = $false },
    [pscustomobject]@{ Name = 'preview names are rejected'; Packages = @(
        (New-Package 'Microsoft.WindowsAppRuntime.2-preview3' '2.5.0.0'),
        (New-Package 'MicrosoftCorporationII.WinAppRuntime.Main.2-p3' '2.5.0.0'),
        (New-Package 'MicrosoftCorporationII.WinAppRuntime.Singleton-p3' '8002.5.0.0'),
        (New-Package 'Microsoft.WinAppRuntime.DDLM.2.5.0.0-x6-p3' '2.5.0.0'));
        Expected = $false }
)

foreach ($case in $cases) {
    $actual = Test-QuailWindowsAppRuntime -Packages $case.Packages -MinimumVersion $minimum
    if ($actual -ne $case.Expected) { throw "Case failed: $($case.Name). Expected $($case.Expected), got $actual." }
}

Write-Output "PASS Windows App Runtime detection cases=$($cases.Count)"
