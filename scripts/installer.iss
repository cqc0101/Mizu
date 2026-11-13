#ifndef Runtime
  #define Runtime "win-x64"
#endif
#ifndef AppVersion
  #define AppVersion "0.2.3"
#endif
#ifndef PackageDir
  #define PackageDir "..\artifacts\Mizu-" + AppVersion + "-" + Runtime
#endif
#ifndef OutputDirPath
  #define OutputDirPath "..\artifacts\installers"
#endif

[Setup]
AppId={{7481F09E-89BC-44F4-9A51-66F31CDB4869}
AppName=Mizu
AppVersion={#AppVersion}
AppPublisher=Mizu contributors
AppPublisherURL=https://github.com/cqc0101/Mizu
AppSupportURL=https://github.com/cqc0101/Mizu/issues
AppUpdatesURL=https://github.com/cqc0101/Mizu/releases
DefaultDirName={localappdata}\Programs\Mizu
DefaultGroupName=Mizu
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
MinVersion=10.0.14393
#if Runtime == "win-x64"
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
#elif Runtime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif
OutputDir={#OutputDirPath}
OutputBaseFilename=Mizu-{#AppVersion}-{#Runtime}-setup
SetupIconFile=..\src\Mizu.Widget\Assets\mizu.ico
UninstallDisplayIcon={app}\Mizu.exe
LicenseFile=..\LICENSE
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Mizu"; Filename: "{app}\Mizu.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Mizu"; Filename: "{app}\Mizu.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Mizu.exe"; Description: "Launch Mizu"; Flags: nowait postinstall skipifsilent unchecked

; Settings and the shared PawnIO driver are intentionally retained on uninstall.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Mizu', StartupCommand) then
      if CompareText(StartupCommand, '"' + ExpandConstant('{app}\app\Mizu.exe') + '"') = 0 then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Mizu');
end;
