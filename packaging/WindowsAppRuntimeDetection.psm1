function Get-QuailPackageProperty([object] $Package, [string] $Name) {
    $property = $Package.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-QuailStableX64CurrentUserPackage([object] $Package) {
    $registeredForCurrentUser = Get-QuailPackageProperty $Package 'RegisteredForCurrentUser'
    if ($null -ne $registeredForCurrentUser -and $registeredForCurrentUser -ne $true) { return $false }

    $architecture = [string](Get-QuailPackageProperty $Package 'Architecture')
    if ($architecture -ne 'X64') { return $false }

    $status = [string](Get-QuailPackageProperty $Package 'Status')
    if ($status -ne 'Ok') { return $false }

    $isDevelopmentMode = Get-QuailPackageProperty $Package 'IsDevelopmentMode'
    return $null -eq $isDevelopmentMode -or $isDevelopmentMode -eq $false
}

function ConvertTo-QuailVersion([object] $Value) {
    try { return [version]$Value }
    catch { return $null }
}

function Test-QuailStableRuntimePackage([object[]] $Packages, [string] $Name, [version] $MinimumVersion) {
    foreach ($package in $Packages) {
        if (-not (Test-QuailStableX64CurrentUserPackage $package)) { continue }
        if ((Get-QuailPackageProperty $package 'Name') -ne $Name) { continue }

        $version = ConvertTo-QuailVersion (Get-QuailPackageProperty $package 'Version')
        if ($null -ne $version -and $version.Major -eq $MinimumVersion.Major -and $version -ge $MinimumVersion) { return $true }
    }

    return $false
}

function Test-QuailStableSingletonPackage([object[]] $Packages) {
    foreach ($package in $Packages) {
        if ((Get-QuailPackageProperty $package 'Name') -eq 'MicrosoftCorporationII.WinAppRuntime.Singleton' -and
            (Test-QuailStableX64CurrentUserPackage $package)) { return $true }
    }

    return $false
}

function Test-QuailStableDdlmPackage([object[]] $Packages, [version] $MinimumVersion) {
    foreach ($package in $Packages) {
        if (-not (Test-QuailStableX64CurrentUserPackage $package)) { continue }

        $match = [regex]::Match([string](Get-QuailPackageProperty $package 'Name'), '^Microsoft\.WinAppRuntime\.DDLM\.(?<version>\d+\.\d+\.\d+\.\d+)-x6$')
        if (-not $match.Success) { continue }

        $releaseVersion = ConvertTo-QuailVersion $match.Groups['version'].Value
        $packageVersion = ConvertTo-QuailVersion (Get-QuailPackageProperty $package 'Version')
        if ($null -ne $releaseVersion -and $null -ne $packageVersion -and
            $releaseVersion -eq $packageVersion -and
            $releaseVersion.Major -eq $MinimumVersion.Major -and
            $releaseVersion -ge $MinimumVersion) { return $true }
    }

    return $false
}

function Test-QuailWindowsAppRuntime {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object[]] $Packages,
        [Parameter(Mandatory)][version] $MinimumVersion
    )

    return (Test-QuailStableRuntimePackage $Packages 'Microsoft.WindowsAppRuntime.2' $MinimumVersion) -and
        (Test-QuailStableRuntimePackage $Packages 'MicrosoftCorporationII.WinAppRuntime.Main.2' $MinimumVersion) -and
        (Test-QuailStableSingletonPackage $Packages) -and
        (Test-QuailStableDdlmPackage $Packages $MinimumVersion)
}
