#ifndef AppVersion
  #error AppVersion must be supplied by build-installers.ps1.
#endif

#ifndef SourceDir
  #error SourceDir must be supplied by build-installers.ps1.
#endif

#ifndef OutputDir
  #error OutputDir must be supplied by build-installers.ps1.
#endif

#ifndef DotNetDesktopUrl
  #error DotNetDesktopUrl must be supplied by build-installers.ps1.
#endif

#ifndef DotNetDesktopSha256
  #error DotNetDesktopSha256 must be supplied by build-installers.ps1.
#endif

#ifndef DotNetDesktopMinimumVersion
  #error DotNetDesktopMinimumVersion must be supplied by build-installers.ps1.
#endif

#ifndef DotNetRuntimeMajor
  #error DotNetRuntimeMajor must be supplied by build-installers.ps1.
#endif

#ifndef DotNetAllowsMajorRollForward
  #error DotNetAllowsMajorRollForward must be supplied by build-installers.ps1.
#endif

#ifndef WindowsAppRuntimeUrl
  #error WindowsAppRuntimeUrl must be supplied by build-installers.ps1.
#endif

#ifndef WindowsAppRuntimeSha256
  #error WindowsAppRuntimeSha256 must be supplied by build-installers.ps1.
#endif

#ifndef WindowsAppRuntimeMinimumVersion
  #error WindowsAppRuntimeMinimumVersion must be supplied by build-installers.ps1.
#endif

#ifndef VcRedistUrl
  #error VcRedistUrl must be supplied by build-installers.ps1.
#endif

#ifndef VcRedistSha256
  #error VcRedistSha256 must be supplied by build-installers.ps1.
#endif

#ifndef VcRedistMinimumVersion
  #error VcRedistMinimumVersion must be supplied by build-installers.ps1.
#endif

#define AppName "Quail M09 Framework-dependent Fixture"

[Setup]
AppId={{C9A91B5B-5B6F-49B9-9F88-DF4EE96BDF46}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\Quail M09 Framework-dependent Fixture
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Quail-M09-FrameworkDependent-{#AppVersion}-Setup
MinVersion=10.0.22000
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
UninstallDisplayName={#AppName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Code]
const
  RequiredDesktopRuntime = '{#DotNetDesktopMinimumVersion}';
  RequiredDesktopRuntimeMajor = {#DotNetRuntimeMajor};
  AllowsDesktopRuntimeMajorRollForward = {#DotNetAllowsMajorRollForward};
  RequiredWindowsAppRuntime = '{#WindowsAppRuntimeMinimumVersion}';
  RequiredVcRedist = '{#VcRedistMinimumVersion}';

function IsVersionAtLeast(const Candidate, Minimum: String): Boolean;
var
  CandidateParts: TArrayOfString;
  MinimumParts: TArrayOfString;
  Index: Integer;
  CandidateValue: Integer;
  MinimumValue: Integer;
begin
  CandidateParts := StringSplit(Candidate, ['.'], stExcludeEmpty);
  MinimumParts := StringSplit(Minimum, ['.'], stExcludeEmpty);
  Result := True;

  for Index := 0 to 3 do
  begin
    CandidateValue := 0;
    MinimumValue := 0;
    if Index < GetArrayLength(CandidateParts) then CandidateValue := StrToIntDef(CandidateParts[Index], 0);
    if Index < GetArrayLength(MinimumParts) then MinimumValue := StrToIntDef(MinimumParts[Index], 0);
    if CandidateValue > MinimumValue then Exit;
    if CandidateValue < MinimumValue then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function NormalizeRegistryVersion(const Value: String): String;
begin
  if (Length(Value) > 0) and ((Value[1] = 'v') or (Value[1] = 'V')) then
    Result := Copy(Value, 2, Length(Value) - 1)
  else
    Result := Value;
end;

function IsRequiredDesktopRuntimeFamily(const Candidate: String): Boolean;
var
  CandidateParts: TArrayOfString;
begin
  CandidateParts := StringSplit(Candidate, ['.'], stExcludeEmpty);
  Result := (GetArrayLength(CandidateParts) > 0) and
    ((StrToIntDef(CandidateParts[0], -1) = RequiredDesktopRuntimeMajor) or
     (AllowsDesktopRuntimeMajorRollForward <> 0));
end;

function HasDesktopRuntime: Boolean;
var
  FindData: TFindRec;
begin
  Result := False;
  if not FindFirst(
    ExpandConstant('{autopf}\dotnet\shared\Microsoft.WindowsDesktop.App\*'),
    FindData) then Exit;

  try
  begin
    repeat
    begin
      if ((FindData.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
        (FindData.Name <> '.') and (FindData.Name <> '..') and
        IsVersionAtLeast(FindData.Name, RequiredDesktopRuntime) and
        IsRequiredDesktopRuntimeFamily(FindData.Name) then
      begin
        Result := True;
        Exit;
      end;
    end;
    until not FindNext(FindData);
  end;
  finally
    FindClose(FindData);
  end;
end;

function RunPowerShellCheck(const Script: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function HasWindowsAppRuntime: Boolean;
begin
  Result := RunPowerShellCheck(
    '$minimum = [version]''' + RequiredWindowsAppRuntime + '''; ' +
    '$required = @(' +
      '@{ Name=''Microsoft.WindowsAppRuntime.2''; Minimum=$true }, ' +
      '@{ Name=''MicrosoftCorporationII.WinAppRuntime.Main.2''; Minimum=$true }, ' +
      '@{ Name=''MicrosoftCorporationII.WinAppRuntime.Singleton''; Minimum=$false }, ' +
      '@{ Name=''Microsoft.WinAppRuntime.DDLM.2.4.0.0-x6''; Minimum=$false }); ' +
    'foreach ($requirement in $required) { ' +
      '$packages = @(Get-AppxPackage -AllUsers -Name $requirement.Name | Where-Object { $_.Architecture -eq ''X64'' -and $_.Status -eq ''Ok'' }); ' +
      'if ($packages.Count -lt 1) { exit 1 }; ' +
      'if ($requirement.Minimum -and -not ($packages | Where-Object { [version]$_.Version -ge $minimum })) { exit 1 } }; ' +
    'exit 0');
end;

function HasVcRedist: Boolean;
var
  Installed: Cardinal;
  Version: String;
begin
  Result := RegQueryDWordValue(HKLM64,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and
    (Installed = 1) and
    RegQueryStringValue(HKLM64,
      'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Version', Version) and
    IsVersionAtLeast(NormalizeRegistryVersion(Version), RequiredVcRedist);
end;

function DownloadAndRun(const Name, Url, Sha256, Parameters: String): Boolean;
var
  InstallerPath: String;
  ResultCode: Integer;
begin
  try
    DownloadTemporaryFile(Url, Name, Sha256, nil);
    InstallerPath := AddBackslash(ExpandConstant('{tmp}')) + Name;
  except
    MsgBox('Could not download or verify the required ' + Name + ' from its Microsoft source. Quail was not installed.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  if not Exec(InstallerPath, Parameters, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Could not start the required ' + Name + '. Quail was not installed.', mbCriticalError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := (ResultCode = 0) or (ResultCode = 3010);
  if not Result then
    MsgBox(Name + ' failed with exit code ' + IntToStr(ResultCode) + '. Quail was not installed.', mbCriticalError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not HasDesktopRuntime then
  begin
    if not DownloadAndRun('windowsdesktop-runtime-10.0.11-win-x64.exe', '{#DotNetDesktopUrl}', '{#DotNetDesktopSha256}', '/install /quiet /norestart') or not HasDesktopRuntime then
    begin
      Result := 'The required .NET 10 Desktop Runtime is unavailable. Quail was not installed.';
      Exit;
    end;
  end;

  if not HasVcRedist then
  begin
    if not DownloadAndRun('VC_redist.x64.exe', '{#VcRedistUrl}', '{#VcRedistSha256}', '/install /quiet /norestart') or not HasVcRedist then
    begin
      Result := 'The required Visual C++ Redistributable is unavailable. Quail was not installed.';
      Exit;
    end;
  end;

  if not HasWindowsAppRuntime then
  begin
    if not DownloadAndRun('WindowsAppRuntimeInstall-x64.exe', '{#WindowsAppRuntimeUrl}', '{#WindowsAppRuntimeSha256}', '--quiet') or not HasWindowsAppRuntime then
    begin
      Result := 'The required Windows App Runtime is unavailable. Quail was not installed.';
      Exit;
    end;
  end;
end;
