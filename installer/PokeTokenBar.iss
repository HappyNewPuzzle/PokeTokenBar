#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\publish\win-x64"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\release"
#endif

[Setup]
AppId={{A2CA032E-240F-4AC8-8EA2-743755BE0FC4}
AppName=PokeTokenBar
AppVersion={#MyAppVersion}
AppPublisher=PokeTokenBar
AppPublisherURL=https://github.com/chattymin/PokeTokenBar
DefaultDirName={localappdata}\Programs\PokeTokenBar
DefaultGroupName=PokeTokenBar
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=PokeTokenBar-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\PokeTokenBar.exe
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\PokeTokenBar"; Filename: "{app}\PokeTokenBar.exe"
Name: "{autodesktop}\PokeTokenBar"; Filename: "{app}\PokeTokenBar.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\PokeTokenBar.exe"; Description: "Launch PokeTokenBar"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RegDeleteValue(HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run', 'PokeTokenBar');
end;
