; Inno Setup script for the AutoDraw add-in (Inventor 2025).
; Build with build-installer.ps1, which compiles the add-in first and passes the version.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define BuildDir "..\bin\Release\net8.0-windows"

[Setup]
AppId={{a9b81ed6-b9f6-4133-a40b-956fc7d5f490}
AppName=AutoDraw for Inventor
AppVersion={#AppVersion}
AppPublisher=JanTrueno
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\AutoDraw.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
LicenseFile=..\LICENSE
SetupIconFile=AutoDraw.ico
UninstallDisplayIcon={app}\AutoDraw.ico
UninstallDisplayName=AutoDraw for Inventor 2025
WizardStyle=modern
WizardImageFile=wizard-large.bmp
WizardSmallImageFile=wizard-small.bmp
OutputDir=..\dist
OutputBaseFilename=AutoDraw-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=no

[Files]
Source: "..\deploy\PackageContents.xml";      DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.txt";         DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE";                         DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "AutoDraw.ico";                       DestDir: "{app}"; Flags: ignoreversion
Source: "..\deploy\AutoDraw.Inventor.addin";  DestDir: "{app}\Contents\2025"; Flags: ignoreversion
Source: "{#BuildDir}\AutoDraw.dll";           DestDir: "{app}\Contents\2025"; Flags: ignoreversion
Source: "{#BuildDir}\AutoDraw.pdb";           DestDir: "{app}\Contents\2025"; Flags: ignoreversion
Source: "{#BuildDir}\AutoDraw.deps.json";     DestDir: "{app}\Contents\2025"; Flags: ignoreversion

[Code]
function InventorRunning(): Boolean;
var
  ResultCode: Integer;
begin
  // tasklist exits 0 either way; findstr sets the result when Inventor.exe is listed
  Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq Inventor.exe" | findstr /I "Inventor.exe" >NUL',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := ResultCode = 0;
end;

function WaitForInventor(): Boolean;
begin
  Result := True;
  while InventorRunning() do
  begin
    if MsgBox('Inventor is running. Close Inventor, then click OK to continue.', mbInformation, MB_OKCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := WaitForInventor();
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForInventor();
end;
