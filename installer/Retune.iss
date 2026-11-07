; Retune for Windows: per-user installer (no administrator rights, no UAC prompt).
; Built by tools/package.ps1, which passes /DAppVersion=... and /DPublishDir=... .

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point at the dotnet publish output
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\dist"
#endif

[Setup]
AppId={{6B3F1E62-8C1A-4E7B-9D55-2A9C7F3B1D40}
AppName=Retune
AppVersion={#AppVersion}
AppVerName=Retune {#AppVersion}
AppPublisher=Retune
AppPublisherURL=https://github.com/0xAliRaza/retune
AppSupportURL=https://github.com/0xAliRaza/retune/issues
AppCopyright=GPL-3.0-or-later
VersionInfoVersion={#AppVersion}
; Installs for the current user only, so it never needs administrator rights.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Retune
DefaultGroupName=Retune
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10 2004 or later (the media-controls API Retune uses).
MinVersion=10.0.19041
WizardStyle=modern
SetupIconFile=..\src\Retune.App\Assets\Retune.ico
UninstallDisplayIcon={app}\Retune.exe
UninstallDisplayName=Retune
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=Retune-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
; Close a running Retune before replacing or removing its files.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Retune"; Filename: "{app}\Retune.exe"
Name: "{autodesktop}\Retune"; Filename: "{app}\Retune.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Retune.exe"; Description: "{cm:LaunchProgram,Retune}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PurgeUserData: Boolean;

// Playlists, history and saved passwords belong to the user, so they stay unless the user asks to remove them.
// Silent uninstalls keep them unless /PURGEDATA is passed.
function InitializeUninstall(): Boolean;
begin
  if UninstallSilent() then
    PurgeUserData := Pos('/PURGEDATA', Uppercase(GetCmdTail())) > 0
  else
    PurgeUserData := MsgBox('Also delete your Retune data?' + #13#10#13#10 +
      'This removes your playlists, favorites, watch history, settings and the passwords Retune saved in ' +
      'Windows Credential Manager. Choose No to keep them for a later reinstall.',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ExitCode: Integer;
begin
  if (CurUninstallStep = usUninstall) and PurgeUserData then
    Exec(ExpandConstant('{app}\Retune.exe'), '--purge-data', '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
end;
