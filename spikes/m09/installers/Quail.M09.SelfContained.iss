#ifndef AppVersion
  #error AppVersion must be supplied by build-installers.ps1.
#endif

#ifndef SourceDir
  #error SourceDir must be supplied by build-installers.ps1.
#endif

#ifndef OutputDir
  #error OutputDir must be supplied by build-installers.ps1.
#endif

#define AppName "Quail M09 Self-contained Fixture"

[Setup]
AppId={{95CDB1D6-5347-4C2A-AB9C-2137122070F0}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\Quail M09 Self-contained Fixture
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=Quail-M09-SelfContained-{#AppVersion}-Setup
MinVersion=10.0.22000
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
PrivilegesRequired=admin
UninstallDisplayName={#AppName}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
