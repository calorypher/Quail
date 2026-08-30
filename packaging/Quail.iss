#ifndef AppVersion
  #error AppVersion must be supplied by scripts/build-installer.ps1.
#endif

#ifndef SourceDir
  #error SourceDir must be supplied by scripts/build-installer.ps1.
#endif

#ifndef OutputDir
  #error OutputDir must be supplied by scripts/build-installer.ps1.
#endif

#ifndef DotNetDesktopUrl
  #error DotNetDesktopUrl must be supplied by scripts/build-installer.ps1.
#endif

#ifndef DotNetDesktopSha256
  #error DotNetDesktopSha256 must be supplied by scripts/build-installer.ps1.
#endif

#ifndef DotNetDesktopMinimumVersion
  #error DotNetDesktopMinimumVersion must be supplied by scripts/build-installer.ps1.
#endif

#ifndef DotNetRuntimeMajor
  #error DotNetRuntimeMajor must be supplied by scripts/build-installer.ps1.
#endif

#ifndef DotNetAllowsMajorRollForward
  #error DotNetAllowsMajorRollForward must be supplied by scripts/build-installer.ps1.
#endif

#ifndef WindowsAppRuntimeUrl
  #error WindowsAppRuntimeUrl must be supplied by scripts/build-installer.ps1.
#endif

#ifndef WindowsAppRuntimeSha256
  #error WindowsAppRuntimeSha256 must be supplied by scripts/build-installer.ps1.
#endif

#ifndef WindowsAppRuntimeMinimumVersion
  #error WindowsAppRuntimeMinimumVersion must be supplied by scripts/build-installer.ps1.
#endif

#ifndef WindowsAppRuntimeCheckBase64
  #error WindowsAppRuntimeCheckBase64 must be supplied by scripts/build-installer.ps1.
#endif

#ifndef VcRedistUrl
  #error VcRedistUrl must be supplied by scripts/build-installer.ps1.
#endif

#ifndef VcRedistSha256
  #error VcRedistSha256 must be supplied by scripts/build-installer.ps1.
#endif

#ifndef VcRedistMinimumVersion
  #error VcRedistMinimumVersion must be supplied by scripts/build-installer.ps1.
#endif

#define AppName "Quail"
#define AppDirectory "{autopf}\\Quail"

[Setup]
AppId={{D67D6288-D90A-429F-9FFD-D1EE472E5D43}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Quail
DefaultDirName={#AppDirectory}
DisableDirPage=yes
UsePreviousAppDir=no
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Quail-{#AppVersion}-Setup
MinVersion=10.0.22000
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
ChangesEnvironment=yes
RedirectionGuard=yes
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
CloseApplications=yes
CloseApplicationsFilter=Quail.exe,Quail.Cli.exe
RestartApplications=no

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Quail"; Filename: "{app}\Quail.exe"; WorkingDir: "{app}"

[Code]
const
  EnvironmentKey = 'SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Environment';
  EnvironmentValue = 'Path';
  WM_SETTINGCHANGE = $001A;
  SMTO_ABORTIFHUNG = $0002;
  RequiredDesktopRuntime = '{#DotNetDesktopMinimumVersion}';
  RequiredDesktopRuntimeMajor = {#DotNetRuntimeMajor};
  AllowsDesktopRuntimeMajorRollForward = {#DotNetAllowsMajorRollForward};
  RequiredWindowsAppRuntime = '{#WindowsAppRuntimeMinimumVersion}';
  RequiredVcRedist = '{#VcRedistMinimumVersion}';
  QuailUninstallRegistryKey = 'SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{D67D6288-D90A-429F-9FFD-D1EE472E5D43}_is1';

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
  finally
    FindClose(FindData);
  end;
end;

function RunPowerShellCheck(const EncodedScript: String): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -EncodedCommand ' + EncodedScript,
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function HasWindowsAppRuntime: Boolean;
begin
  Result := RunPowerShellCheck('{#WindowsAppRuntimeCheckBase64}');
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

function CanonicalPath(const Value: String): String;
begin
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Value));
end;

function CanonicalInstallationDirectory: String;
begin
  Result := CanonicalPath(ExpandConstant('{autopf}\Quail'));
end;

function IsReparsePoint(const Path: String): Boolean;
var
  FindData: TFindRec;
begin
  Result := False;
  if not FindFirst(Path, FindData) then Exit;

  try
    Result := (FindData.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0;
  finally
    FindClose(FindData);
  end;
end;

function ContainsReparsePoint(const Directory: String): Boolean;
var
  FindData: TFindRec;
  ChildPath: String;
begin
  Result := False;
  if not FindFirst(AddBackslash(Directory) + '*', FindData) then Exit;

  try
    repeat
    begin
      if (FindData.Name <> '.') and (FindData.Name <> '..') then
      begin
        ChildPath := PathCombine(Directory, FindData.Name);
        if (FindData.Attributes and FILE_ATTRIBUTE_REPARSE_POINT) <> 0 then
        begin
          Result := True;
          Exit;
        end;

        if ((FindData.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           ContainsReparsePoint(ChildPath) then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
    until not FindNext(FindData);
  finally
    FindClose(FindData);
  end;
end;

function HasReparsePointInDestinationPath(const Destination: String): Boolean;
var
  CurrentPath: String;
  ParentPath: String;
begin
  Result := False;
  CurrentPath := CanonicalPath(Destination);

  while CurrentPath <> '' do
  begin
    if FileOrDirExists(CurrentPath) and IsReparsePoint(CurrentPath) then
    begin
      Result := True;
      Exit;
    end;

    ParentPath := RemoveBackslashUnlessRoot(ExtractFileDir(CurrentPath));
    if (ParentPath = '') or PathSame(ParentPath, CurrentPath) then Exit;
    CurrentPath := ParentPath;
  end;
end;

function IsDirectoryEmpty(const Directory: String): Boolean;
var
  FindData: TFindRec;
begin
  Result := True;
  if not FindFirst(AddBackslash(Directory) + '*', FindData) then Exit;

  try
    repeat
    begin
      if (FindData.Name <> '.') and (FindData.Name <> '..') then
      begin
        Result := False;
        Exit;
      end;
    end;
    until not FindNext(FindData);
  finally
    FindClose(FindData);
  end;
end;

function GetRegisteredQuailInstallation(var InstallLocation, InstalledVersion: String): Boolean;
var
  UninstallCommand: String;
begin
  Result := False;
  if not RegQueryStringValue(HKLM64, QuailUninstallRegistryKey,
    'InstallLocation', InstallLocation) then Exit;
  if not RegQueryStringValue(HKLM64, QuailUninstallRegistryKey,
    'UninstallString', UninstallCommand) then Exit;
  if not RegQueryStringValue(HKLM64, QuailUninstallRegistryKey,
    'DisplayVersion', InstalledVersion) then Exit;

  Result := (InstallLocation <> '') and (InstalledVersion <> '') and
    (UninstallCommand <> '');
end;

function ValidateFixedInstallationContract: String;
var
  Destination: String;
  EffectiveDestination: String;
  CanonicalDestination: String;
  RegisteredLocation: String;
  RegisteredVersion: String;
begin
  Result := '';
  Destination := CanonicalPath(WizardDirValue);
  EffectiveDestination := CanonicalPath(ExpandConstant('{app}'));
  CanonicalDestination := CanonicalInstallationDirectory;

  if not PathSame(Destination, CanonicalDestination) or
     not PathSame(EffectiveDestination, CanonicalDestination) then
  begin
    Result := 'Quail installs only to ' + CanonicalDestination + '.';
    Exit;
  end;

  if HasReparsePointInDestinationPath(Destination) then
  begin
    Result := 'Quail cannot install through a reparse-point destination path.';
    Exit;
  end;

  if GetRegisteredQuailInstallation(RegisteredLocation, RegisteredVersion) then
  begin
    if not PathSame(CanonicalPath(RegisteredLocation), CanonicalDestination) then
    begin
      Result := 'Uninstall the previous Quail development build before installing this version.';
      Exit;
    end;

    if not SameText(RegisteredVersion, '{#AppVersion}') then
    begin
      Result := 'Uninstall the previous Quail development build before installing this version.';
      Exit;
    end;
  end;

  if not DirExists(Destination) or IsDirectoryEmpty(Destination) then Exit;

  if not GetRegisteredQuailInstallation(RegisteredLocation, RegisteredVersion) then
  begin
    Result := 'Quail will not overwrite a non-empty directory that is not a recognized same-version Quail installation.';
    Exit;
  end;

  if ContainsReparsePoint(Destination) then
    Result := 'Quail cannot update an installation containing reparse points.';
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
  Result := ValidateFixedInstallationContract;
  if Result <> '' then Exit;

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

function SendMessageTimeout(
  hWnd: HWND;
  Msg: UINT;
  wParam: Longint;
  lParam: String;
  fuFlags: UINT;
  uTimeout: UINT;
  var lpdwResult: DWORD): LRESULT;
  external 'SendMessageTimeoutW@user32.dll stdcall';

function NormalizePathEntry(const Value: String): String;
begin
  Result := Trim(Value);

  if (Length(Result) >= 2) and (Result[1] = '"') and
     (Result[Length(Result)] = '"') then
  begin
    Result := Copy(Result, 2, Length(Result) - 2);
  end;

  Result := RemoveBackslashUnlessRoot(Result);
end;

function RemoveQuailPathEntries(const ExistingPath, TargetPath: String): String;
var
  Entries: TArrayOfString;
  Index: Integer;
  KeptCount: Integer;
begin
  Entries := StringSplit(ExistingPath, [';'], stAll);
  Result := '';
  KeptCount := 0;

  for Index := 0 to GetArrayLength(Entries) - 1 do
  begin
    if CompareText(NormalizePathEntry(Entries[Index]), NormalizePathEntry(TargetPath)) <> 0 then
    begin
      if KeptCount > 0 then
      begin
        Result := Result + ';';
      end;

      Result := Result + Entries[Index];
      KeptCount := KeptCount + 1;
    end;
  end;
end;

procedure BroadcastEnvironmentChange;
var
  MessageResult: DWORD;
begin
  SendMessageTimeout(
    HWND_BROADCAST,
    WM_SETTINGCHANGE,
    0,
    'Environment',
    SMTO_ABORTIFHUNG,
    5000,
    MessageResult);
end;

procedure SetQuailPathEntry(const IncludeQuail: Boolean);
var
  ExistingPath: String;
  UpdatedPath: String;
  TargetPath: String;
begin
  TargetPath := CanonicalInstallationDirectory;
  if not RegQueryStringValue(HKLM, EnvironmentKey, EnvironmentValue, ExistingPath) then
  begin
    RaiseException('Could not read the system PATH.');
  end;

  UpdatedPath := RemoveQuailPathEntries(ExistingPath, TargetPath);
  if IncludeQuail then
  begin
    if UpdatedPath <> '' then
    begin
      UpdatedPath := UpdatedPath + ';';
    end;

    UpdatedPath := UpdatedPath + TargetPath;
  end;

  if not RegWriteExpandStringValue(HKLM, EnvironmentKey, EnvironmentValue, UpdatedPath) then
  begin
    RaiseException('Could not update the system PATH.');
  end;

  BroadcastEnvironmentChange;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    SetQuailPathEntry(True);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    SetQuailPathEntry(False);
  end;
end;
