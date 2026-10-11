; Installateur SharpSaw (Inno Setup 6).
; Compilation : scripts\build-installer.ps1  (ou ISCC.exe /DAppVersion=1.0.0 installer\SharpSaw.iss)
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceExe
  #define SourceExe "..\dist\SharpSaw.exe"
#endif

[Setup]
AppId={{6F3B8E1A-4C52-4D7B-9A0E-5B2D1C7E9F48}
AppName=SharpSaw
AppVersion={#AppVersion}
AppVerName=SharpSaw {#AppVersion}
AppPublisher=Ayi NEDJIMI Consultants
AppPublisherURL=https://ayinedjimi-consultants.fr
AppSupportURL=https://github.com/ayinedjimi/SharpSaw/issues
DefaultDirName={autopf}\SharpSaw
DefaultGroupName=SharpSaw
UninstallDisplayIcon={app}\SharpSaw.exe
SetupIconFile=..\SharpSaw.ico
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=SharpSaw-Setup-{#AppVersion}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
PrivilegesRequired=admin
Compression=lzma2/fast
SolidCompression=no
WizardStyle=modern
DisableProgramGroupPage=yes

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedsVCRedist

[Icons]
Name: "{autoprograms}\SharpSaw"; Filename: "{app}\SharpSaw.exe"
Name: "{autodesktop}\SharpSaw"; Filename: "{app}\SharpSaw.exe"; Tasks: desktopicon

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installation du runtime Visual C++ (requis)..."; Check: NeedsVCRedist
Filename: "{app}\SharpSaw.exe"; Description: "{cm:LaunchProgram,SharpSaw}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Composants extraits par l'application au premier lancement
Type: filesandordirs; Name: "{localappdata}\SharpSaw\runtime"

[Code]
function NeedsVCRedist: Boolean;
var
  Installed: Cardinal;
begin
  Result := not (RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) and (Installed = 1));
end;
