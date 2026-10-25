; Installateur de Monolith Hub (Inno Setup 6).
; Compilé par build.ps1, qui fournit AppVersion et SourceDir :
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\artifacts\app installer\MonolithHub.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif

#define AppName "Monolith Hub"
#define AppExe "MonolithHub.exe"
#define AppPublisher "ValentinMglc"
#define AppUrl "https://github.com/ValentinMglc/monolith-hub"

[Setup]
; Identifiant fixe : ne jamais le changer, il relie les mises à jour à l'installation existante.
AppId={{5B8E2C1A-7F3D-4E9B-A6C2-9D41F0E7B3A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppComments=Monitoring et éclairage pour PC HP OMEN. Non affilié à HP Inc.
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; L'application elle-même demande les droits administrateur (capteurs, tâche de démarrage).
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\artifacts
OutputBaseFilename=Monolith-Hub-Setup-{#AppVersion}
SetupIconFile=..\MonolithHub\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; Ferme Monolith Hub s'il tourne (mise à jour ou désinstallation).
CloseApplications=force
RestartApplications=no

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
french.DesktopIconTask=Créer un raccourci sur le Bureau
english.DesktopIconTask=Create a desktop shortcut
french.LaunchApp=Lancer Monolith Hub
english.LaunchApp=Launch Monolith Hub
french.RemoveSettings=Voulez-vous aussi supprimer vos réglages Monolith Hub (profils, couleurs, seuils d'alerte) ?
english.RemoveSettings=Do you also want to delete your Monolith Hub settings (profiles, colors, alert thresholds)?

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; shellexec : l'application demande elle-même l'élévation via son manifeste.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Avant de supprimer les fichiers : arrêter Monolith Hub puis retirer sa tâche de démarrage
// (et, si l'utilisateur le souhaite, ses réglages dans %APPDATA%).
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  Args: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Args := '--uninstall';
    if not UninstallSilent and
       (MsgBox(CustomMessage('RemoveSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
      Args := Args + ' --purge';
    Exec(ExpandConstant('{app}\{#AppExe}'), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
