#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif

#define AppName "Monolith Hub"
#define AppExe "MonolithHub.exe"
#define AppPublisher "ValentinMglc"
#define AppUrl "https://github.com/ValentinMglc/MonolithHub"

[Setup]
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
french.PawnIOGroup=Pilote facultatif (gratuit, signé et open source) :
english.PawnIOGroup=Optional driver (free, signed and open source):
french.PawnIOTask=Installer PawnIO pour afficher la température, la fréquence et la puissance du processeur
english.PawnIOTask=Install PawnIO to show the processor temperature, clock and power
french.PawnIOStatus=Installation du pilote PawnIO…
english.PawnIOStatus=Installing the PawnIO driver…

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIconTask}"; Flags: unchecked
Name: "pawnio"; Description: "{cm:PawnIOTask}"; GroupDescription: "{cm:PawnIOGroup}"; Check: PawnIONeeded

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Parameters: "--install-pawnio"; StatusMsg: "{cm:PawnIOStatus}"; Tasks: pawnio; Flags: runhidden waituntilterminated
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function PawnIONeeded: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

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
