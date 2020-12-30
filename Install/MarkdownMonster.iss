#define MyAppName "Markdown Monster"
#define Moniker "markdownmonster"
#define MyAppVersion GetFileVersion('.\Distribution\MarkdownMonster.exe')
#define MyAppPublisher "West Wind Technologies"
#define MyAppURL "https://markdownmonster.west-wind.com"
#define MyAppExeName "MarkdownMonster.exe"
#define MySetupImageIco "..\MarkdownMonster\MarkdownMonster.ico"
#define MyAppCopyright "Copyright (c) 2016-2020, West Wind Technologies"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
; Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{E3476879-4D00-405A-B058-90D4AEAD7C4A}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
VersionInfoVersion={#MyAppVersion}
AppCopyright={#MyAppCopyright}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
;ArchitecturesInstallIn64BitMode=x64
DefaultDirName={localappdata}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=.\license.txt
OutputDir=.\Builds\CurrentRelease
OutputBaseFilename=MarkdownMonsterSetup
SetupIconFile={#MySetupImageIco}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
ChangesAssociations=yes
; 55x58
WizardSmallImageFile=WizIcon.bmp
; 164 x 314
WizardImageFile=WizBanner.bmp
CloseApplicationsFilter=*.exe
;CloseApplicationsFilter=*.exe,*.dll,*.chm,*.ttf
PrivilegesRequired=admin
AlwaysShowDirOnReadyPage=yes
DisableDirPage=false
                                                              
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked;

[Icons]
Name: "{commonprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; 

[Files]
Source: ".\Distribution\MarkdownMonster.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: ".\Distribution\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
;Source: ".\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
;Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe";

[Registry]
; File Association for .md and .markdown
Root: HKCU; Subkey: "Software\Classes\{#MyAppName}";                    ValueData: "Program {#MyAppName}";  Flags: uninsdeletekey;   ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\{#MyAppName}\DefaultIcon";        ValueData: "{app}\{#MyAppExeName},0";               ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\{#MyAppName}\shell\open\command"; ValueData: """{app}\{#MyAppExeName}"" ""%1""";  ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\.md";                             ValueData: "{#MyAppName}";          Flags: uninsdeletevalue; ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\.markdown";                       ValueData: "{#MyAppName}";          Flags: uninsdeletevalue; ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\.mdcrypt";                       ValueData: "{#MyAppName}";          Flags: uninsdeletevalue; ValueType: string;  ValueName: ""
Root: HKCU; Subkey: "Software\Classes\.mdproj";                       ValueData: "{#MyAppName}";          Flags: uninsdeletevalue; ValueType: string;  ValueName: ""

; this requires ADMIN rights!
Root: HKCR; Subkey: "{#Moniker}";                    ValueData: "URL:{#MyAppName}";  Flags: uninsdeletekey;   ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#Moniker}";                    ValueName: "URL Protocol";  ValueData: "";  ValueType: string;  
Root: HKCR; Subkey: "{#Moniker}\DefaultIcon";        ValueData: "{app}\{#MyAppExeName},0";               ValueType: string;  ValueName: ""
Root: HKCR; Subkey: "{#Moniker}\shell\open\command"; ValueData: """{app}\{#MyAppExeName}"" ""%1""";  ValueType: string;  ValueName: ""

; IE 11 mode
Root: HKCU; Subkey: "Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"; ValueType: dword; ValueName: "MarkdownMonster.exe"; ValueData: "11001"; Flags: createvalueifdoesntexist
Root: HKCU; Subkey: "Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_GPU_RENDERING"; ValueType: dword; ValueName: "MarkdownMonster.exe"; ValueData: "1"; Flags: createvalueifdoesntexist

; Add MM to user's system path
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{localappdata}\{#MyAppName}" ; Check: NeedsAddPath('{localappdata}\{#MyAppName}')

[Code]
function GetUninstallString: string;
var
  sUnInstPath: string;
  sUnInstallString: String;
begin
  Result := '';
  sUnInstPath := ExpandConstant('Software\Microsoft\Windows\CurrentVersion\Uninstall\{{E3476879-4D00-405A-B058-90D4AEAD7C4A}_is1'); //Your App GUID/ID
  sUnInstallString := '';
  if not RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString) then
    RegQueryStringValue(HKCU, sUnInstPath, 'UninstallString', sUnInstallString);
  Result := sUnInstallString;
end;

function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
  ParamExpanded: string;
begin
  //expand the setup constants like {app} from Param
  ParamExpanded := ExpandConstant(Param);
  if not RegQueryStringValue(HKEY_CURRENT_USER,
    'Environment',
    'Path', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  // look for the path with leading and trailing semicolon and with or without \ ending
  // Pos() returns 0 if not found
  Result := Pos(';' + UpperCase(ParamExpanded) + ';', ';' + UpperCase(OrigPath) + ';') = 0;  
  if Result = True then
     Result := Pos(';' + UpperCase(ParamExpanded) + '\;', ';' + UpperCase(OrigPath) + ';') = 0; 
end;

function IsUpgrade: Boolean; 
begin
  Result := (GetUninstallString() <> '');
end;

function InitializeSetup: Boolean;
var
  V: Integer;
  iResultCode: Integer;
  sUnInstallString: string;
begin
  Result := True; // in case when no previous version is found
  if RegValueExists(HKEY_CURRENT_USER,'Software\Microsoft\Windows\CurrentVersion\Uninstall\{{E3476879-4D00-405A-B058-90D4AEAD7C4A}_is1', 'UninstallString') then  //Your App GUID/ID
  begin
  //  V := MsgBox(ExpandConstant('Hey! An old version of app was detected. Do you want to uninstall it?'), mbInformation, MB_YESNO); //Custom Message if App installed
  //  if V = IDYES then
  //  begin
      sUnInstallString := GetUninstallString();
      sUnInstallString :=  RemoveQuotes(sUnInstallString);
      Exec(ExpandConstant(sUnInstallString), '/SILENT', '', SW_SHOW, ewWaitUntilTerminated, iResultCode);
      Result := True; //if you want to proceed after uninstall
  //  Exit; //if you want to quit after uninstall
  //   end
  // else
  //    Result := False; //when older version present and not uninstalled
  end;
end;