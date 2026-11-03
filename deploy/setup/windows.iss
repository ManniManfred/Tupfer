; Windows-Setup für den Tupfer Designer (Inno Setup 6).
; Aufruf aus dem Hauptverzeichnis nach „dotnet publish … -r win-x64 -o publish/win-x64“:
;   iscc /DVersion=1.2.3 /DSource=publish\win-x64 /DOutput=setups deploy\setup\windows.iss

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\..\publish\win-x64"
#endif
#ifndef Output
  #define Output "..\..\setups"
#endif

[Setup]
AppId={{6F3B1D52-8C1E-4D8A-9B57-2E0C7A4F1B93}
AppName=Tupfer Designer
AppVersion={#Version}
AppVerName=Tupfer Designer {#Version}
AppPublisher=Manfred Roßkamp
AppPublisherURL=https://github.com/ManniManfred/Tupfer
DefaultDirName={autopf}\Tupfer Designer
DefaultGroupName=Tupfer Designer
DisableProgramGroupPage=yes
; Ohne Administratorrechte für den eigenen Benutzer installieren, auf Wunsch für alle
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\Tupfer.Designer\Assets\Tupfer.ico
UninstallDisplayIcon={app}\Tupfer.Designer.exe
OutputDir={#Output}
OutputBaseFilename=Tupfer-Designer-Setup-Windows-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Explorer über die neue Dateiverknüpfung informieren
ChangesAssociations=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Tupfer Designer"; Filename: "{app}\Tupfer.Designer.exe"
Name: "{autodesktop}\Tupfer Designer"; Filename: "{app}\Tupfer.Designer.exe"; Tasks: desktopicon

[Registry]
; Projektdateien (*.tupfer) per Doppelklick öffnen; HKA = HKCU oder bei Installation für alle HKLM
Root: HKA; Subkey: "Software\Classes\.tupfer"; ValueType: string; ValueName: ""; ValueData: "TupferDesigner.Projekt"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.tupfer\OpenWithProgids"; ValueType: string; ValueName: "TupferDesigner.Projekt"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\TupferDesigner.Projekt"; ValueType: string; ValueName: ""; ValueData: "Tupfer-Projekt"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\TupferDesigner.Projekt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Tupfer.Designer.exe,0"
Root: HKA; Subkey: "Software\Classes\TupferDesigner.Projekt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Tupfer.Designer.exe"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\Tupfer.Designer.exe\SupportedTypes"; ValueType: string; ValueName: ".tupfer"; ValueData: ""; Flags: uninsdeletekey

[Run]
Filename: "{app}\Tupfer.Designer.exe"; Description: "{cm:LaunchProgram,Tupfer Designer}"; Flags: nowait postinstall skipifsilent
; Automatische Aktualisierung aus dem Programm (/SILENT /NEUSTART=1): danach wieder starten
Filename: "{app}\Tupfer.Designer.exe"; Flags: nowait runasoriginaluser; Check: RestartAfterUpdate

[Code]
function RestartAfterUpdate: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:NEUSTART|0}') = '1');
end;
