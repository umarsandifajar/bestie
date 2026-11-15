; Installer bestie (Inno Setup 6). Dibangun lewat build.bat atau GitHub Actions.
; Perlu: dist\standalone\MySecretary.exe sudah dibuat (dotnet publish self-contained).
#ifndef MyAppVersion
  #define MyAppVersion "1.1.0"
#endif
#define MyAppName "bestie"
#define MyAppExe "MySecretary.exe"

[Setup]
AppId={{5CF73F7C-F11C-4421-B436-38D7E960FDA2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher=sandiana.my.id
AppPublisherURL=https://sandiana.my.id
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\bestie
DefaultGroupName=bestie
DisableProgramGroupPage=yes
; Pasang per-pengguna (tanpa admin) secara default; pengguna boleh memilih untuk semua pengguna.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Jika bestie sedang berjalan, installer meminta ditutup dulu.
AppMutex=MySecretary.Single
CloseApplications=yes
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
OutputDir=..\dist
OutputBaseFilename=bestie-v{#MyAppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "startup"; Description: "Jalankan bestie otomatis saat Windows menyala"; GroupDescription: "Opsi tambahan:"
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Opsi tambahan:"; Flags: unchecked

[Files]
Source: "..\dist\standalone\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\bestie"; Filename: "{app}\{#MyAppExe}"
Name: "{autodesktop}\bestie"; Filename: "{app}\{#MyAppExe}"; Tasks: desktopicon

[Registry]
; Jalan otomatis saat login Windows (muncul di Task Manager > Startup apps, bisa dimatikan di sana).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "bestie"; ValueData: """{app}\{#MyAppExe}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExe}"; Description: "Jalankan bestie sekarang"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if MsgBox('Hapus juga pengaturan dan data bestie (To Do, Notes, reminder)?' + #13#10 +
              'Pilih Tidak jika ingin menginstal ulang nanti dengan data yang sama.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{userappdata}\MySecretary'), True, True, True);
end;
