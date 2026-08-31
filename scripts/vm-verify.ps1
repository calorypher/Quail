[CmdletBinding()]
param(
    [string] $VmName = 'Quail-Lab',
    [Parameter(Mandatory)]
    [string] $VmUser,
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string[]] $ArtifactPath,
    [ValidateSet('CliStatus', 'ProtectedIndexRuntime')]
    [string] $Scenario = 'CliStatus',
    [string] $DataVolumeLabel = 'QUAIL_LAB_DATA',
    [string] $RemoteRoot = 'C:/Temp/Quail-Verify',
    [ValidateRange(1, 600)]
    [int] $TimeoutSeconds = 90
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $PSScriptRoot 'Private.QuailLab.psm1') -Force
$logPath = New-QuailToolingLog $repositoryRoot 'vm-verify'

function Fail([string] $Reason) {
    $compactReason = ($Reason -replace '[\r\n]+', ' ').Trim()
    Write-Output "FAIL reason=$compactReason log=$logPath"
    exit 1
}

function Invoke-IndexWorker($Connection, [string] $VmUser, [string] $LogPath, [string] $Executable, [string] $MountPoint, [string] $VolumeIdentity, [string] $Operation = 'Refresh') {
    $operationId = [Guid]::NewGuid()
    $safeExecutable = $Executable.Replace("'", "''")
    $safeMountPoint = $MountPoint.Replace("'", "''")
    $safeIdentity = $VolumeIdentity.Replace("'", "''")
    $safeOperation = $Operation.Replace("'", "''")
    $remoteScript = @"
`$operationId = [Guid]'$operationId'
`$process = Start-Process -FilePath '$safeExecutable' -ArgumentList @('--internal-index-operation', '$safeOperation', '--internal-operation-id', `$operationId.ToString(), '--internal-mount-point', '$safeMountPoint', '--internal-volume-identity', '$safeIdentity') -Wait -PassThru
`$exitCode = `$process.ExitCode
if (`$exitCode -notin @(0, 3)) { throw "ProtectedIndex worker failed; operation=$safeOperation exit=`$exitCode" }
[pscustomobject]@{ Operation = '$safeOperation'; ExitCode = `$exitCode } | ConvertTo-Json -Compress
"@
    return Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath $remoteScript
}

function Invoke-ObservedIndexWorker($Connection, [string] $VmUser, [string] $LogPath, [string] $Executable, [string] $MountPoint, [string] $VolumeIdentity, [string] $Database, [string] $Operation) {
    $operationId = [Guid]::NewGuid()
    $safeExecutable = $Executable.Replace("'", "''")
    $safeMountPoint = $MountPoint.Replace("'", "''")
    $safeIdentity = $VolumeIdentity.Replace("'", "''")
    $safeDatabase = $Database.Replace("'", "''")
    $safeOperation = $Operation.Replace("'", "''")
    $remoteScript = @"
`$ErrorActionPreference = 'Stop'
`$database = '$safeDatabase'
`$paths = [ordered]@{
    Db = `$database
    Journal = `$database + '-journal'
    Wal = `$database + '-wal'
    Shm = `$database + '-shm'
    Building = `$database + '.building'
    BuildingJournal = `$database + '.building-journal'
    BuildingWal = `$database + '.building-wal'
    BuildingShm = `$database + '.building-shm'
    Previous = `$database + '.previous'
    Lock = Join-Path (Join-Path (Split-Path -Parent (Split-Path -Parent `$database)) 'Locks') (([IO.Path]::GetFileNameWithoutExtension(`$database)) + '.lock')
}
function Get-StorageState {
    `$state = [ordered]@{}
    foreach (`$item in `$paths.GetEnumerator()) { `$state[`$item.Key] = Test-Path -LiteralPath `$item.Value }
    return [pscustomobject] `$state
}
`$before = Get-StorageState
`$during = [ordered]@{}
foreach (`$key in `$paths.Keys) { `$during[`$key] = `$false }
`$arguments = @('--internal-index-operation', '$safeOperation', '--internal-operation-id', '$operationId', '--internal-mount-point', '$safeMountPoint', '--internal-volume-identity', '$safeIdentity')
`$process = Start-Process -FilePath '$safeExecutable' -ArgumentList `$arguments -PassThru
do {
    `$sample = Get-StorageState
    foreach (`$key in `$paths.Keys) { if (`$sample.`$key) { `$during[`$key] = `$true } }
    Start-Sleep -Milliseconds 20
} until (`$process.HasExited)
`$process.WaitForExit()
`$exitCode = `$process.ExitCode
if (`$exitCode -notin @(0, 3)) { throw "ProtectedIndex worker failed; operation=$safeOperation exit=`$exitCode" }
`$after = Get-StorageState
if (-not `$after.Db -or `$after.Journal -or `$after.Wal -or `$after.Shm -or `$after.Building -or `$after.BuildingJournal -or `$after.BuildingWal -or `$after.BuildingShm -or `$after.Previous -or -not `$after.Lock) {
    throw "ProtectedIndex quiescent storage state is invalid after $safeOperation`: `$(`$after | ConvertTo-Json -Compress)"
}
[pscustomobject]@{ Operation = '$safeOperation'; ExitCode = `$exitCode; Before = `$before; During = [pscustomobject] `$during; After = `$after } | ConvertTo-Json -Depth 4 -Compress
"@
    return Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath $remoteScript
}

function Invoke-UnelevatedProtectedIndexRead($Connection, [string] $VmUser, [string] $LogPath, [string] $CliExecutable, [string] $Database, [string] $Query, [string] $ExpectedName, [string] $OutputRoot) {
    foreach ($value in @($CliExecutable, $Database, $Query, $ExpectedName, $OutputRoot)) {
        if ($value.Contains(' ')) { throw 'ProtectedIndex standard-user verification requires paths and query values without spaces.' }
    }
    $safeCli = $CliExecutable.Replace("'", "''")
    $safeDatabase = $Database.Replace("'", "''")
    $safeQuery = $Query.Replace("'", "''")
    $safeExpected = $ExpectedName.Replace("'", "''")
    $safeOutputRoot = $OutputRoot.Replace("'", "''")
    $safeHelperAssembly = (Join-Path $OutputRoot 'Quail.ProtectedIndex.AccountRights.dll').Replace("'", "''")
    Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath @"
`$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath '$safeHelperAssembly')) {
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
public static class QuailAccountRights {
 [StructLayout(LayoutKind.Sequential)] struct Attributes { public int Length; public IntPtr Root,Object; public uint Flags; public IntPtr Descriptor,Quality; }
 [StructLayout(LayoutKind.Sequential)] struct Text { public ushort Length,MaximumLength; public IntPtr Buffer; }
 [DllImport("advapi32.dll")] static extern uint LsaOpenPolicy(IntPtr system,ref Attributes attributes,uint access,out IntPtr policy);
 [DllImport("advapi32.dll")] static extern uint LsaAddAccountRights(IntPtr policy,IntPtr sid,Text[] rights,uint count);
 [DllImport("advapi32.dll")] static extern uint LsaRemoveAccountRights(IntPtr policy,IntPtr sid,bool all,Text[] rights,uint count);
 [DllImport("advapi32.dll")] static extern uint LsaNtStatusToWinError(uint status);
 [DllImport("advapi32.dll")] static extern uint LsaClose(IntPtr policy);
 public static void Set(string sidText,string right,bool add) {
  var sid=new SecurityIdentifier(sidText); var bytes=new byte[sid.BinaryLength]; sid.GetBinaryForm(bytes,0); IntPtr sidPointer=Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes,0,sidPointer,bytes.Length);
  IntPtr buffer=Marshal.StringToHGlobalUni(right); IntPtr policy=IntPtr.Zero;
  try { var attributes=new Attributes(); attributes.Length=Marshal.SizeOf(attributes); uint status=LsaOpenPolicy(IntPtr.Zero,ref attributes,0x810,out policy); if(status!=0) Fail(status);
   var rights=new[]{new Text{Length=(ushort)(right.Length*2),MaximumLength=(ushort)((right.Length+1)*2),Buffer=buffer}}; status=add?LsaAddAccountRights(policy,sidPointer,rights,1):LsaRemoveAccountRights(policy,sidPointer,false,rights,1); if(status!=0) Fail(status);
  } finally { if(policy!=IntPtr.Zero)LsaClose(policy); Marshal.FreeHGlobal(buffer); Marshal.FreeHGlobal(sidPointer); }
 }
 static void Fail(uint status) { throw new InvalidOperationException("lsa:"+LsaNtStatusToWinError(status)); }
}
'@ -OutputAssembly '$safeHelperAssembly'
}
"@
    $accountOutput = Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath @"
`$ErrorActionPreference = 'Stop'
Add-Type -Path '$safeHelperAssembly'
`$username = 'qverify' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
`$password = 'Q!a1' + [Guid]::NewGuid().ToString('N').Substring(0, 10)
`$readerRoot = Join-Path '$safeOutputRoot' ('standard-reader-' + [Guid]::NewGuid().ToString('N'))
`$sid = `$null
try {
    & net.exe user `$username `$password /add /expires:never | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw 'Unable to create the temporary ProtectedIndex standard user.' }
    `$sidObject = (Get-LocalUser -Name `$username -ErrorAction Stop).Sid
    `$sid = `$sidObject.Value
    [QuailAccountRights]::Set(`$sid, 'SeBatchLogonRight', `$true)
    New-Item -ItemType Directory -Path `$readerRoot | Out-Null
    `$acl = Get-Acl -LiteralPath `$readerRoot
    `$rule = New-Object Security.AccessControl.FileSystemAccessRule -ArgumentList @(`$sidObject, [Security.AccessControl.FileSystemRights]::Modify, ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit), [Security.AccessControl.PropagationFlags]::None, [Security.AccessControl.AccessControlType]::Allow)
    `$acl.AddAccessRule(`$rule); Set-Acl -LiteralPath `$readerRoot -AclObject `$acl
    [pscustomobject]@{ Username=`$username; Password=`$password; Sid=`$sid; ReaderRoot=`$readerRoot } | ConvertTo-Json -Compress
} catch { if (`$sid) { [QuailAccountRights]::Set(`$sid, 'SeBatchLogonRight', `$false) }; & net.exe user `$username /delete | Out-Null; throw }
"@
    $account = $accountOutput | Select-Object -Last 1 | ConvertFrom-Json
    $safeUsername = $account.Username.Replace("'", "''")
    $safePassword = $account.Password.Replace("'", "''")
    $safeSid = $account.Sid.Replace("'", "''")
    $safeReaderRoot = $account.ReaderRoot.Replace("'", "''")
    $workingDirectory = (Split-Path -Parent $CliExecutable).Replace("'", "''")
    $probe = ((Split-Path -Parent $Database) + '\standard-reader-write-probe.tmp').Replace("'", "''")
    function Invoke-StandardProcess([string] $CommandLine, [string] $OutputName) {
        $safeCommand = $CommandLine.Replace("'", "''")
        $safeOutput = (Join-Path $account.ReaderRoot $OutputName).Replace("'", "''")
        $output = Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath @"
`$taskName = 'Quail-ProtectedIndex-Reader-' + [Guid]::NewGuid().ToString('N')
`$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument '/d /s /c $safeCommand > $safeOutput 2>&1' -WorkingDirectory '$workingDirectory'
`$principal = New-ScheduledTaskPrincipal -UserId "`$env:COMPUTERNAME\$safeUsername" -LogonType Password -RunLevel Limited
`$task = New-ScheduledTask -Action `$action -Principal `$principal
try {
    Register-ScheduledTask -TaskName `$taskName -InputObject `$task -User "`$env:COMPUTERNAME\$safeUsername" -Password '$safePassword' | Out-Null
    Start-ScheduledTask -TaskName `$taskName
    `$deadline = [DateTime]::UtcNow.AddSeconds(30)
    do { Start-Sleep -Milliseconds 200; `$state=(Get-ScheduledTask `$taskName).State; `$info=Get-ScheduledTaskInfo `$taskName; if([DateTime]::UtcNow -ge `$deadline){throw 'Timed out waiting for the ProtectedIndex standard-user task.'} } while(`$info.LastRunTime.Year -lt 2000 -or `$state -eq 'Running')
} finally { Unregister-ScheduledTask -TaskName `$taskName -Confirm:`$false -ErrorAction SilentlyContinue }
`$text = if(Test-Path -LiteralPath '$safeOutput'){[IO.File]::ReadAllText('$safeOutput')}else{''}
[pscustomobject]@{ExitCode=`$info.LastTaskResult;Text=`$text}|ConvertTo-Json -Compress
"@
        return $output | Select-Object -Last 1 | ConvertFrom-Json
    }
    try {
        $token = Invoke-StandardProcess 'whoami /groups /fo csv' 'token.txt'
        if ($token.ExitCode -ne 0 -or $token.Text -notmatch 'S-1-16-8192' -or $token.Text -match 'S-1-16-12288') { throw "Standard-user child is not verified medium integrity: exit=$($token.ExitCode); output=$($token.Text)" }
        $writeProbe = Invoke-StandardProcess "echo probe>$probe & if exist $probe (exit /b 0) else (exit /b 5)" 'probe.txt'
        $probeExists = Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath "Test-Path -LiteralPath '$probe'" | Select-Object -Last 1
        if ($writeProbe.ExitCode -ne 5 -or $probeExists -ne 'False') { throw "Medium-integrity protected-child probe was not denied: exit=$($writeProbe.ExitCode); exists=$probeExists" }
        $status = Invoke-StandardProcess "$safeCli status --index $safeDatabase" 'status.txt'
        if ($status.ExitCode -ne 0 -or $status.Text -notmatch 'state=complete') { throw "Unelevated status failed: $($status.Text)" }
        $search = Invoke-StandardProcess "$safeCli search --index $safeDatabase $safeQuery" 'search.txt'
        if ($search.ExitCode -ne 0 -or $search.Text -notmatch [regex]::Escape($ExpectedName)) { throw "Unelevated search failed: $($search.Text)" }
        return [pscustomobject]@{ Account='temporary-standard-user'; Integrity='medium'; WriteProbeExit=$writeProbe.ExitCode; StatusExit=$status.ExitCode; SearchExit=$search.ExitCode; Status=$status.Text.Trim(); Search=$search.Text.Trim() } | ConvertTo-Json -Compress
    }
    finally {
        Invoke-QuailRemotePowerShell $Connection $VmUser $LogPath @"
Add-Type -Path '$safeHelperAssembly'
if (Test-Path -LiteralPath '$probe') { [IO.File]::Delete('$probe') }
[QuailAccountRights]::Set('$safeSid', 'SeBatchLogonRight', `$false)
Get-CimInstance Win32_UserProfile -Filter "SID='$safeSid'" -ErrorAction SilentlyContinue | Remove-CimInstance -ErrorAction SilentlyContinue
& net.exe user '$safeUsername' /delete | Out-Null
if (Test-Path -LiteralPath '$safeReaderRoot') { Remove-Item -LiteralPath '$safeReaderRoot' -Recurse -Force }
"@ | Out-Null
    }
}

try {
    $resolvedRepositoryRoot = (Resolve-Path -LiteralPath $repositoryRoot -ErrorAction Stop).Path.TrimEnd('\')
    $artifacts = @()
    foreach ($requestedPath in $ArtifactPath) {
        if ([IO.Path]::IsPathRooted($requestedPath)) {
            throw 'ArtifactPath must be repository-relative.'
        }

        $candidatePath = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $requestedPath))
        if (-not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            throw "ArtifactPath does not name a file: $requestedPath"
        }

        $resolvedPath = (Resolve-Path -LiteralPath $candidatePath -ErrorAction Stop).Path
        if (-not $resolvedPath.StartsWith("$resolvedRepositoryRoot\", [StringComparison]::OrdinalIgnoreCase)) {
            throw 'ArtifactPath must resolve inside the repository.'
        }

        $artifacts += [pscustomobject]@{
            RequestedPath = $requestedPath
            FullPath = $resolvedPath
            Name = Split-Path -Leaf $resolvedPath
            Hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPath).Hash.ToLowerInvariant()
        }
    }

    $collision = @($artifacts | Group-Object -Property Name | Where-Object { $_.Count -gt 1 })
    if ($collision.Count -gt 0) {
        throw "ArtifactPath remote-name collision: $($collision[0].Name)"
    }
    foreach ($artifact in $artifacts | Sort-Object Name) {
        Write-QuailToolingLog $logPath "artifact path=$($artifact.RequestedPath) name=$($artifact.Name) sha256=$($artifact.Hash)"
    }

    $connection = Wait-QuailLabSsh $VmName $VmUser $repositoryRoot $logPath $TimeoutSeconds
    $environment = Get-QuailLabDataVolume $connection $VmUser $logPath $DataVolumeLabel
    $safeRemoteRoot = $RemoteRoot.Replace("'", "''")
    Invoke-QuailRemotePowerShell $connection $VmUser $logPath "New-Item -ItemType Directory -Force -Path '$safeRemoteRoot' | Out-Null" | Out-Null

    foreach ($artifact in $artifacts) {
        Invoke-QuailLoggedExternal $logPath 'scp' @(
            @('-q') + $connection.SshOptions + @($artifact.FullPath, "$VmUser@$($connection.IpAddress)`:$RemoteRoot/")
        ) | Out-Null
        $safeRemoteArtifact = (Join-Path $RemoteRoot $artifact.Name).Replace("'", "''")
        $remoteHash = (Invoke-QuailRemotePowerShell $connection $VmUser $logPath "(Get-FileHash -Algorithm SHA256 -LiteralPath '$safeRemoteArtifact').Hash.ToLowerInvariant()" | Select-Object -Last 1).Trim()
        if ($artifact.Hash -ne $remoteHash) {
            throw "SHA-256 mismatch for $($artifact.Name)."
        }
        Write-QuailToolingLog $logPath "remote artifact name=$($artifact.Name) sha256=$remoteHash"
    }

    $scenarioArtifact = $artifacts[0]
    switch ($Scenario) {
        'CliStatus' {
            $safeRemoteArtifact = (Join-Path $RemoteRoot $scenarioArtifact.Name).Replace("'", "''")
            $smokeDatabase = "$($environment.DriveLetter):\\.quail-tooling-smoke.db".Replace("'", "''")
            $smokeOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath "& '$safeRemoteArtifact' status --index '$smokeDatabase'"
            if (-not ($smokeOutput | Where-Object { $_ -match '^STATUS source=.+ state=' })) {
                throw 'CliStatus did not return the expected STATUS summary.'
            }
        }
        'ProtectedIndexRuntime' {
            if ($artifacts.Count -ne 1 -or [IO.Path]::GetExtension($scenarioArtifact.Name) -ne '.zip') {
                throw 'ProtectedIndexRuntime requires exactly one self-contained publish ZIP artifact.'
            }

            $localPublishDirectory = Join-Path (Split-Path -Parent $scenarioArtifact.FullPath) 'self-contained'
            $localExecutable = Join-Path $localPublishDirectory 'Quail.exe'
            if (-not (Test-Path -LiteralPath $localExecutable -PathType Leaf)) {
                throw 'ProtectedIndexRuntime requires sibling self-contained\Quail.exe for extracted-file integrity verification.'
            }
            $localExecutableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $localExecutable).Hash.ToLowerInvariant()
            $safeArchive = (Join-Path $RemoteRoot $scenarioArtifact.Name).Replace("'", "''")
            $safePublish = (Join-Path $RemoteRoot 'published').Replace("'", "''")
            $publishOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
if (Test-Path -LiteralPath '$safePublish') { throw 'ProtectedIndex publish destination already exists.' }
Expand-Archive -LiteralPath '$safeArchive' -DestinationPath '$safePublish'
`$executable = Join-Path '$safePublish' 'Quail.exe'
if (-not (Test-Path -LiteralPath `$executable -PathType Leaf)) { throw 'ProtectedIndex final Quail.exe is missing after archive extraction.' }
`$cli = Join-Path '$safePublish' 'Quail.Cli.exe'
if (-not (Test-Path -LiteralPath `$cli -PathType Leaf)) { throw 'ProtectedIndex final Quail.Cli.exe is missing after archive extraction.' }
(Get-FileHash -Algorithm SHA256 -LiteralPath `$executable).Hash.ToLowerInvariant()
"@
            $remoteExecutableHash = ($publishOutput | Select-Object -Last 1).Trim()
            if ($remoteExecutableHash -ne $localExecutableHash) {
                throw 'ProtectedIndex extracted Quail.exe SHA-256 mismatch.'
            }
            Write-QuailToolingLog $logPath "ProtectedIndex final executable sha256=$remoteExecutableHash"

            $setup = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$appData = [Environment]::GetFolderPath('LocalApplicationData')
`$catalogPath = Join-Path `$appData 'Quail\indexes.json'
`$identity = @(& mountvol '$($environment.DriveLetter):' /L | Where-Object { `$_.Trim() } | Select-Object -Last 1)[0].Trim().TrimEnd('\')
if ([string]::IsNullOrWhiteSpace(`$identity)) { throw 'ProtectedIndex volume identity is unavailable.' }
if (Test-Path -LiteralPath `$catalogPath) {
    `$existingCatalog = Get-Content -LiteralPath `$catalogPath -Raw | ConvertFrom-Json
    `$existingEntries = @(`$existingCatalog.Entries)
    if (`$existingCatalog.Version -ne 1 -or `$existingEntries.Count -ne 1 -or `$existingEntries[0].VolumeIdentity.TrimEnd('\') -ne `$identity -or `$existingEntries[0].MountPoint -ne '$($environment.DriveLetter):\') {
        throw 'Existing ProtectedIndex lab catalog is not the exact supported QUAIL_LAB_DATA entry.'
    }
}
`$sha256 = [Security.Cryptography.SHA256]::Create()
try { `$hash = `$sha256.ComputeHash([Text.Encoding]::UTF8.GetBytes(`$identity.ToUpperInvariant())) } finally { `$sha256.Dispose() }
`$name = (([BitConverter]::ToString(`$hash[0..11])) -replace '-', '').ToLowerInvariant()
`$database = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) "Quail\Indexes\volume-`$name.db"
`$databaseExisted = Test-Path -LiteralPath `$database
foreach (`$path in @(
    `$database,
    (`$database + '-journal'),
    (`$database + '-wal'),
    (`$database + '-shm'),
    (`$database + '.building'),
    (`$database + '.building-journal'),
    (`$database + '.building-wal'),
    (`$database + '.building-shm'),
    (`$database + '.previous'))) {
    if (Test-Path -LiteralPath `$path) {
        `$item = Get-Item -LiteralPath `$path -Force
        if ((`$item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 -or `$item.PSIsContainer) { throw "Refusing to reset unexpected ProtectedIndex storage object: `$path" }
        [IO.File]::Delete(`$path)
    }
}
`$fixture = '$($environment.DriveLetter):\Quail-ProtectedIndex-$([Guid]::NewGuid().ToString('N'))'
New-Item -ItemType Directory -Path `$fixture | Out-Null
Set-Content -LiteralPath (Join-Path `$fixture 'protected-index-initial.txt') -Value 'initial'
New-Item -ItemType Directory -Path (Split-Path -Parent `$catalogPath) -Force | Out-Null
@{ Version = 1; Entries = @(@{ VolumeIdentity = `$identity; MountPoint = '$($environment.DriveLetter):\'; DatabasePath = `$database; EnabledForSearch = `$false }) } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath `$catalogPath -Encoding utf8
[pscustomobject]@{ Identity = `$identity; Database = `$database; DatabaseExisted = `$databaseExisted; Fixture = `$fixture; Executable = (Join-Path '$safePublish' 'Quail.exe') } | ConvertTo-Json -Compress
"@
            $runtime = $setup | Select-Object -Last 1 | ConvertFrom-Json

            $buildOutput = Invoke-ObservedIndexWorker $connection $VmUser $logPath $runtime.Executable "$($environment.DriveLetter):\" $runtime.Identity $runtime.Database 'Build'
            $build = $buildOutput | Select-Object -Last 1 | ConvertFrom-Json

            $remoteCli = Join-Path $safePublish 'Quail.Cli.exe'
            $buildReadOutput = Invoke-UnelevatedProtectedIndexRead $connection $VmUser $logPath $remoteCli $runtime.Database 'protected-index-initial' 'protected-index-initial.txt' $RemoteRoot
            $buildRead = $buildReadOutput | Select-Object -Last 1 | ConvertFrom-Json

            Invoke-QuailRemotePowerShell $connection $VmUser $logPath "Set-Content -LiteralPath '$($runtime.Fixture.Replace("'", "''"))\protected-index-refresh-old.txt' -Value 'refresh'; Rename-Item -LiteralPath '$($runtime.Fixture.Replace("'", "''"))\protected-index-refresh-old.txt' -NewName 'protected-index-refresh-renamed.txt'" | Out-Null
            $syncOutput = Invoke-ObservedIndexWorker $connection $VmUser $logPath $runtime.Executable "$($environment.DriveLetter):\" $runtime.Identity $runtime.Database 'Refresh'
            $sync = $syncOutput | Select-Object -Last 1 | ConvertFrom-Json
            $refreshReadOutput = Invoke-UnelevatedProtectedIndexRead $connection $VmUser $logPath $remoteCli $runtime.Database 'protected-index-refresh-renamed' 'protected-index-refresh-renamed.txt' $RemoteRoot
            $refreshRead = $refreshReadOutput | Select-Object -Last 1 | ConvertFrom-Json
            $zeroSyncOutput = Invoke-ObservedIndexWorker $connection $VmUser $logPath $runtime.Executable "$($environment.DriveLetter):\" $runtime.Identity $runtime.Database 'Refresh'
            $zeroSync = $zeroSyncOutput | Select-Object -Last 1 | ConvertFrom-Json
            $zeroReadOutput = Invoke-UnelevatedProtectedIndexRead $connection $VmUser $logPath $remoteCli $runtime.Database 'protected-index-refresh-renamed' 'protected-index-refresh-renamed.txt' $RemoteRoot
            $zeroRead = $zeroReadOutput | Select-Object -Last 1 | ConvertFrom-Json
            if ($zeroRead.Status -notmatch 'lastRefreshedUtc=(?!n/a)') { throw 'ProtectedIndex zero-change refresh did not persist freshness.' }
            if (@(Invoke-QuailRemotePowerShell $connection $VmUser $logPath '@(Get-Process -Name Quail -ErrorAction SilentlyContinue).Count' | Select-Object -Last 1) -ne 0) { throw 'ProtectedIndex orphan Quail worker remains.' }
            Write-QuailToolingLog $logPath "ProtectedIndex storage build=$($build | ConvertTo-Json -Depth 4 -Compress) refresh=$($sync | ConvertTo-Json -Depth 4 -Compress) zeroRefresh=$($zeroSync | ConvertTo-Json -Depth 4 -Compress)"
            Write-QuailToolingLog $logPath "ProtectedIndex unelevated build=$($buildRead | ConvertTo-Json -Compress) refresh=$($refreshRead | ConvertTo-Json -Compress) zeroRefresh=$($zeroRead | ConvertTo-Json -Compress)"
            $securityOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$database = '$($runtime.Database.Replace("'", "''"))'
`$fixture = '$($runtime.Fixture.Replace("'", "''"))'
`$indexes = Split-Path -Parent `$database
`$root = Split-Path -Parent `$indexes
`$outside = Join-Path `$fixture 'adversarial-targets'
New-Item -ItemType Directory -Path `$outside -Force | Out-Null

function Invoke-WorkerRaw([string] `$Operation) {
    `$operationId = [Guid]::NewGuid()
    `$arguments = @('--internal-index-operation', `$Operation, '--internal-operation-id', `$operationId.ToString(), '--internal-mount-point', `$mount, '--internal-volume-identity', `$identity)
    `$process = Start-Process -FilePath `$executable -ArgumentList `$arguments -Wait -PassThru
    return `$process.ExitCode
}

`$rootAcl = Get-Acl -LiteralPath `$root
`$indexesAcl = Get-Acl -LiteralPath `$indexes
if (-not `$rootAcl.AreAccessRulesProtected -or -not `$indexesAcl.AreAccessRulesProtected) { throw 'Protected storage ACL inheritance is not disabled.' }
if (`$rootAcl.Owner -notmatch 'Administrators|SYSTEM' -or `$indexesAcl.Owner -notmatch 'Administrators|SYSTEM') { throw 'Protected storage owner is not trusted.' }

`$fileTarget = Join-Path `$outside 'file-target.txt'
Set-Content -LiteralPath `$fileTarget -Value 'file-target-unchanged'
`$fileHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath `$fileTarget).Hash
`$databaseBackup = `$database + '.security-backup'
Move-Item -LiteralPath `$database -Destination `$databaseBackup
try {
    New-Item -ItemType SymbolicLink -Path `$database -Target `$fileTarget | Out-Null
    `$fileSymlinkExit = Invoke-WorkerRaw 'Refresh'
    if (`$fileSymlinkExit -ne 13) { throw "Final DB symlink was not rejected by protected storage; exit=`$fileSymlinkExit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$fileTarget).Hash -ne `$fileHashBefore) { throw 'Final DB symlink target changed.' }
}
finally {
    if (Test-Path -LiteralPath `$database) { [IO.File]::Delete(`$database) }
    Move-Item -LiteralPath `$databaseBackup -Destination `$database
}

[pscustomobject]@{
    RootSddl = `$rootAcl.Sddl
    IndexesSddl = `$indexesAcl.Sddl
    FileSymlinkExit = `$fileSymlinkExit
    FileTargetBefore = `$fileHashBefore
    FileTargetAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath `$fileTarget).Hash
} | ConvertTo-Json -Compress
"@
            $securityFiles = $securityOutput | Select-Object -Last 1 | ConvertFrom-Json

            $securityJournalOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$journal = '$($runtime.Database.Replace("'", "''"))-journal'
`$target = Join-Path '$($runtime.Fixture.Replace("'", "''"))' 'journal-target.txt'
Set-Content -LiteralPath `$target -Value 'journal-target-unchanged'
`$before = (Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash
try {
    New-Item -ItemType SymbolicLink -Path `$journal -Target `$target | Out-Null
    `$arguments = @('--internal-index-operation','Refresh','--internal-operation-id',([Guid]::NewGuid()).ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
    `$exit = (Start-Process -FilePath `$executable -ArgumentList `$arguments -Wait -PassThru).ExitCode
    if (`$exit -ne 13) { throw "Rollback journal symlink was not rejected; exit=`$exit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash -ne `$before) { throw 'Rollback journal symlink target changed.' }
} finally { if (Test-Path -LiteralPath `$journal) { [IO.File]::Delete(`$journal) } }
[pscustomobject]@{ Exit=`$exit; Before=`$before; After=(Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash } | ConvertTo-Json -Compress
"@
            $securityJournal = $securityJournalOutput | Select-Object -Last 1 | ConvertFrom-Json

            $securityStagingOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$database = '$($runtime.Database.Replace("'", "''"))'
`$outside = Join-Path '$($runtime.Fixture.Replace("'", "''"))' 'adversarial-targets'
`$staging = `$database + '.building'
`$target = Join-Path `$outside 'staging-target.txt'
Set-Content -LiteralPath `$target -Value 'staging-target-unchanged'
`$before = (Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash
try {
    New-Item -ItemType SymbolicLink -Path `$staging -Target `$target | Out-Null
    `$id = [Guid]::NewGuid()
    `$args = @('--internal-index-operation','Rebuild','--internal-operation-id',`$id.ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
    `$exit = (Start-Process -FilePath `$executable -ArgumentList `$args -Wait -PassThru).ExitCode
    if (`$exit -ne 13) { throw "Staging symlink was not rejected by protected storage; exit=`$exit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash -ne `$before) { throw 'Staging symlink target changed.' }
}
finally { if (Test-Path -LiteralPath `$staging) { [IO.File]::Delete(`$staging) } }

`$stagingJournal = `$database + '.building-journal'
`$journalTarget = Join-Path `$outside 'staging-journal-target.txt'
Set-Content -LiteralPath `$journalTarget -Value 'staging-journal-target-unchanged'
`$journalBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath `$journalTarget).Hash
try {
    New-Item -ItemType SymbolicLink -Path `$stagingJournal -Target `$journalTarget | Out-Null
    `$id = [Guid]::NewGuid()
    `$args = @('--internal-index-operation','Rebuild','--internal-operation-id',`$id.ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
    `$journalExit = (Start-Process -FilePath `$executable -ArgumentList `$args -Wait -PassThru).ExitCode
    if (`$journalExit -ne 13) { throw "Staging rollback journal symlink was not rejected; exit=`$journalExit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$journalTarget).Hash -ne `$journalBefore) { throw 'Staging rollback journal symlink target changed.' }
}
finally { if (Test-Path -LiteralPath `$stagingJournal) { [IO.File]::Delete(`$stagingJournal) } }
[pscustomobject]@{ Exit = `$exit; Before = `$before; After = (Get-FileHash -Algorithm SHA256 -LiteralPath `$target).Hash; JournalExit = `$journalExit; JournalBefore = `$journalBefore; JournalAfter = (Get-FileHash -Algorithm SHA256 -LiteralPath `$journalTarget).Hash } | ConvertTo-Json -Compress
"@
            $securityStaging = $securityStagingOutput | Select-Object -Last 1 | ConvertFrom-Json

            $securityJunctionOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$database = '$($runtime.Database.Replace("'", "''"))'
`$fixture = '$($runtime.Fixture.Replace("'", "''"))'
`$indexes = Split-Path -Parent `$database
`$root = Split-Path -Parent `$indexes
`$outside = Join-Path `$fixture 'adversarial-targets'
function Invoke-WorkerRaw([string] `$Operation) {
    `$operationId = [Guid]::NewGuid()
    `$arguments = @('--internal-index-operation', `$Operation, '--internal-operation-id', `$operationId.ToString(), '--internal-mount-point', `$mount, '--internal-volume-identity', `$identity)
    return (Start-Process -FilePath `$executable -ArgumentList `$arguments -Wait -PassThru).ExitCode
}

`$indexesTarget = Join-Path `$outside 'indexes-junction-target'
`$indexesSentinel = Join-Path `$indexesTarget 'sentinel.txt'
`$indexesBackup = `$indexes + '.security-backup'
New-Item -ItemType Directory -Path `$indexesTarget | Out-Null
Set-Content -LiteralPath `$indexesSentinel -Value 'indexes-target-unchanged'
`$indexesHashBefore = (Get-FileHash -Algorithm SHA256 -LiteralPath `$indexesSentinel).Hash
Move-Item -LiteralPath `$indexes -Destination `$indexesBackup
try {
    cmd /c mklink /J "`$indexes" "`$indexesTarget" | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw 'Could not create the Indexes junction.' }
    `$indexesJunctionExit = Invoke-WorkerRaw 'Refresh'
    if (`$indexesJunctionExit -ne 13) { throw "Indexes junction was not rejected by protected storage; exit=`$indexesJunctionExit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$indexesSentinel).Hash -ne `$indexesHashBefore) { throw 'Indexes junction target changed.' }
}
finally {
    if (Test-Path -LiteralPath `$indexes) { [IO.Directory]::Delete(`$indexes) }
    Move-Item -LiteralPath `$indexesBackup -Destination `$indexes
}

[pscustomobject]@{ Exit = `$indexesJunctionExit; Before = `$indexesHashBefore; After = (Get-FileHash -Algorithm SHA256 -LiteralPath `$indexesSentinel).Hash } | ConvertTo-Json -Compress
"@
            $securityJunctions = $securityJunctionOutput | Select-Object -Last 1 | ConvertFrom-Json

            $securityRootOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$database = '$($runtime.Database.Replace("'", "''"))'
`$root = Split-Path -Parent (Split-Path -Parent `$database)
`$target = Join-Path '$($runtime.Fixture.Replace("'", "''"))' 'adversarial-targets\root-junction-target'
`$sentinel = Join-Path `$target 'sentinel.txt'
`$backup = `$root + '.security-backup'
New-Item -ItemType Directory -Path `$target | Out-Null
Set-Content -LiteralPath `$sentinel -Value 'root-target-unchanged'
`$before = (Get-FileHash -Algorithm SHA256 -LiteralPath `$sentinel).Hash
Move-Item -LiteralPath `$root -Destination `$backup
try {
    cmd /c mklink /J "`$root" "`$target" | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw 'Could not create the protected-root junction.' }
    `$id = [Guid]::NewGuid()
    `$args = @('--internal-index-operation','Refresh','--internal-operation-id',`$id.ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
    `$exit = (Start-Process -FilePath `$executable -ArgumentList `$args -Wait -PassThru).ExitCode
    if (`$exit -ne 13) { throw "Protected-root junction was not rejected; exit=`$exit" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath `$sentinel).Hash -ne `$before) { throw 'Protected-root junction target changed.' }
}
finally { if (Test-Path -LiteralPath `$root) { [IO.Directory]::Delete(`$root) }; Move-Item -LiteralPath `$backup -Destination `$root }
[pscustomobject]@{ Exit = `$exit; Before = `$before; After = (Get-FileHash -Algorithm SHA256 -LiteralPath `$sentinel).Hash } | ConvertTo-Json -Compress
"@
            $securityRoot = $securityRootOutput | Select-Object -Last 1 | ConvertFrom-Json

            $securityConcurrencyOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$fixture = '$($runtime.Fixture.Replace("'", "''"))'
`$outside = Join-Path `$fixture 'adversarial-targets'
function Invoke-WorkerRaw([string] `$Operation) {
    `$operationId = [Guid]::NewGuid()
    `$arguments = @('--internal-index-operation', `$Operation, '--internal-operation-id', `$operationId.ToString(), '--internal-mount-point', `$mount, '--internal-volume-identity', `$identity)
    return (Start-Process -FilePath `$executable -ArgumentList `$arguments -Wait -PassThru).ExitCode
}

`$resultTarget = Join-Path `$outside 'result-junction-target'
`$adminOperations = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Quail\AdminOperations'
New-Item -ItemType Directory -Path `$resultTarget | Out-Null
if (Test-Path -LiteralPath `$adminOperations) {
    if (@(Get-ChildItem -LiteralPath `$adminOperations -Force).Count -ne 0) { throw 'Legacy AdminOperations path is not empty.' }
    [IO.Directory]::Delete(`$adminOperations)
}
try {
    cmd /c mklink /J "`$adminOperations" "`$resultTarget" | Out-Null
    if (`$LASTEXITCODE -ne 0) { throw 'Could not create the result-path junction.' }
    `$resultTransportExit = Invoke-WorkerRaw 'Refresh'
    if (`$resultTransportExit -ne 0) { throw "Exit-code result transport refresh failed; exit=`$resultTransportExit" }
    if (@(Get-ChildItem -LiteralPath `$resultTarget -Force).Count -ne 0) { throw 'Worker wrote through the legacy result path.' }
}
finally {
    if (Test-Path -LiteralPath `$adminOperations) { [IO.Directory]::Delete(`$adminOperations) }
}

[pscustomobject]@{ Exit = `$resultTransportExit; TargetCount = @(Get-ChildItem -LiteralPath `$resultTarget -Force).Count } | ConvertTo-Json -Compress
"@
            $securityConcurrency = $securityConcurrencyOutput | Select-Object -Last 1 | ConvertFrom-Json

            $concurrencyOutput = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$ErrorActionPreference = 'Stop'
`$executable = '$($runtime.Executable.Replace("'", "''"))'
`$mount = '$($environment.DriveLetter):\'
`$identity = '$($runtime.Identity.Replace("'", "''"))'
`$fixture = Join-Path '$($runtime.Fixture.Replace("'", "''"))' 'concurrency'
New-Item -ItemType Directory -Path `$fixture | Out-Null
for (`$index = 0; `$index -lt 5000; `$index++) { [IO.File]::WriteAllText((Join-Path `$fixture ("file-{0:D5}.txt" -f `$index)), 'x') }
`$args1 = @('--internal-index-operation','Rebuild','--internal-operation-id',([Guid]::NewGuid()).ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
`$args2 = @('--internal-index-operation','Rebuild','--internal-operation-id',([Guid]::NewGuid()).ToString(),'--internal-mount-point',`$mount,'--internal-volume-identity',`$identity)
`$p1 = Start-Process -FilePath `$executable -ArgumentList `$args1 -PassThru
`$p2 = Start-Process -FilePath `$executable -ArgumentList `$args2 -PassThru
`$p1.WaitForExit(); `$p2.WaitForExit()
`$exits = @(`$p1.ExitCode,`$p2.ExitCode) | Sort-Object
if ((`$exits -join ',') -ne '0,13') { throw "Concurrent workers did not fail closed exactly once; exits=`$(`$exits -join ',')" }
if (@(Get-Process -Name Quail -ErrorAction SilentlyContinue).Count -ne 0) { throw 'ProtectedIndex orphan Quail worker remains after concurrency test.' }
[pscustomobject]@{ Exits = (`$exits -join ',') } | ConvertTo-Json -Compress
"@
            $concurrency = $concurrencyOutput | Select-Object -Last 1 | ConvertFrom-Json

            $postSecurityStatus = Invoke-QuailRemotePowerShell $connection $VmUser $logPath @"
`$output = @(& '$remoteCli' status --index '$($runtime.Database.Replace("'", "''"))')
if (`$LASTEXITCODE -ne 0 -or -not (`$output -match 'state=complete')) { throw 'ProtectedIndex status is not complete after security/concurrency scenarios.' }
`$output | Select-Object -Last 1
"@
            $postConcurrencyReadOutput = Invoke-UnelevatedProtectedIndexRead $connection $VmUser $logPath $remoteCli $runtime.Database 'protected-index-refresh-renamed' 'protected-index-refresh-renamed.txt' $RemoteRoot
            $postConcurrencyRead = $postConcurrencyReadOutput | Select-Object -Last 1 | ConvertFrom-Json
            Write-QuailToolingLog $logPath "ProtectedIndex security fileSymlinkExit=$($securityFiles.FileSymlinkExit) journalSymlinkExit=$($securityJournal.Exit) stagingSymlinkExit=$($securityStaging.Exit) stagingJournalSymlinkExit=$($securityStaging.JournalExit) indexesJunctionExit=$($securityJunctions.Exit) rootJunctionExit=$($securityRoot.Exit) resultTransportExit=$($securityConcurrency.Exit) concurrentExitCodes=$($concurrency.Exits) fileTargetBefore=$($securityFiles.FileTargetBefore) fileTargetAfter=$($securityFiles.FileTargetAfter) journalTargetBefore=$($securityJournal.Before) journalTargetAfter=$($securityJournal.After) stagingTargetBefore=$($securityStaging.Before) stagingTargetAfter=$($securityStaging.After) stagingJournalTargetBefore=$($securityStaging.JournalBefore) stagingJournalTargetAfter=$($securityStaging.JournalAfter) indexesTargetBefore=$($securityJunctions.Before) indexesTargetAfter=$($securityJunctions.After) rootTargetBefore=$($securityRoot.Before) rootTargetAfter=$($securityRoot.After)"
            Write-Output "ProtectedIndexRuntime buildExit=$($build.ExitCode) syncExit=$($sync.ExitCode) zeroSyncExit=$($zeroSync.ExitCode) readerIntegrity=$($postConcurrencyRead.Integrity) security=file-symlink,journal-symlink,staging-symlink,staging-journal-symlink,indexes-junction,root-junction,result-transport concurrency=$($concurrency.Exits) status=$($postSecurityStatus | Select-Object -Last 1)"
        }
    }

    $integrityInput = (($artifacts | Sort-Object Name | ForEach-Object { "$($_.Name):$($_.Hash)" }) -join "`n")
    $integritySummary = Get-QuailSha256Text $integrityInput
    Write-Output "PASS vm=$VmName ssh=true volume=$($environment.Label) artifacts=$($artifacts.Count) integritySha256=$integritySummary scenario=$Scenario"
    exit 0
}
catch {
    Write-QuailToolingLog $logPath $_ | Out-Null
    Fail $_.Exception.Message
}
