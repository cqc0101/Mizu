#ifndef FixtureExe
  #error FixtureExe is required
#endif
#ifndef OutputFolder
  #error OutputFolder is required
#endif
[Setup]
AppId={{E65A668B-1BD9-4F4B-8CFB-1CB6D4637256}
AppName=Mizu updater isolated fixture
AppVersion=0.7.12
DefaultDirName={tmp}\Mizu-updater-fixture
PrivilegesRequired=lowest
Uninstallable=no
CreateUninstallRegKey=no
DisableProgramGroupPage=yes
OutputDir={#OutputFolder}
OutputBaseFilename=fixture-setup
Compression=lzma2/fast
SolidCompression=yes
[Files]
Source: "{#FixtureExe}"; DestDir: "{app}\app"; DestName: "Mizu.exe"; Flags: ignoreversion
