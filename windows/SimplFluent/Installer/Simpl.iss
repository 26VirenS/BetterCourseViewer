; Simpl for Windows (windows/SimplFluent), installed: Simpl-Setup.exe, made with Inno Setup 6 (7 reads it too) by CI
; right after `dotnet publish`, from the published folder (.github/workflows/windows-fluent.yml).
;
; Easy on the student: installed for them alone, in %LOCALAPPDATA%\Programs\Simpl, so no administrator's permission is
; asked; no welcome page and one question (a desktop shortcut too?), then the last page with Open Simpl ticked. A Start
; menu shortcut, an entry in Settings > Apps, and an uninstaller that takes the program away and leaves what Simpl keeps
; (%LOCALAPPDATA%\Simpl -- its settings, grade history and web view, never in the program's folder -- and the saved
; sign-in, in Windows' Credential Manager as "Simpl Courses/sign-in").
;
; An update, as Simpl's Settings > Updates > Restart to Update runs it (Support/Updates.cs):
;   Simpl-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /LAUNCH
; waits for Simpl to have quit (asking it to, if it has not), puts the new version in place, and (/LAUNCH) opens it --
; or, if it could not (another Simpl that would not quit, a file it could not replace), opens the version that was there.
;
; Built with:
;   ISCC /DAppVersion=0.2.0.12 "/DAppDisplayVersion=0.2.0 (v012)" /DSourceDir=<dist\Simpl> /DOutputDir=<dist> Simpl.iss
;
; (Plain ASCII on purpose: read the same by every Inno Setup, whatever code page it reads a file without a BOM in.)

#if Ver < EncodeVer(6,0,0)
  #error Inno Setup 6 or later is needed
#endif

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef AppDisplayVersion
  #define AppDisplayVersion AppVersion
#endif
#ifndef SourceDir
  #define SourceDir "..\..\..\dist\Simpl"
#endif
#ifndef OutputDir
  #define OutputDir "..\..\..\dist"
#endif

; The names Simpl itself uses (Program.cs: AppIdentity and Instance) -- the same on both sides, always.
#define AppUserModelID "SimplCourses.Simpl"
#define AppMutexName "SimplCourses.Simpl.App"
#define QuitEventName "Local\SimplCourses.Simpl.Quit"

[Setup]
; (fixed for good: Windows knows the installed Simpl by it, an update finds the copy before it by it, and Simpl's
; Updates reads its uninstall entry by it -- Support/Updates.cs, InstallerAppId)
AppId={{B65F70B7-E13E-4CCC-9E1C-0B659660D130}
AppName=Simpl
AppVersion={#AppVersion}
AppVerName=Simpl {#AppDisplayVersion}
AppPublisher=Simpl Courses
AppPublisherURL=https://simplcourses.com/
AppSupportURL=https://simplcourses.com/report/
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Simpl
VersionInfoProductTextVersion={#AppDisplayVersion}
VersionInfoDescription=Simpl Setup
VersionInfoCompany=Simpl Courses

; for the student alone: no administrator, and the program apart from the files Simpl keeps (%LOCALAPPDATA%\Simpl)
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\Simpl
MinVersion=10.0.17763
#if Ver >= EncodeVer(6,3,0)
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
#endif

; the fewest pages: no welcome, no folder, no Start menu folder, no "ready" -- the desktop shortcut's box (with Install
; under it), the progress, the end
DisableWelcomePage=yes
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
DisableFinishedPage=no
ShowLanguageDialog=no
#if Ver >= EncodeVer(6,6,0)
WizardStyle=modern dynamic windows11
#else
WizardStyle=modern
#endif
SetupIconFile=..\Assets\Simpl.ico
#if Ver >= EncodeVer(6,5,2)
WizardSmallImageFile=..\Assets\Simpl-256.png
#endif

; Simpl open (it stays in the notification area when its window is closed) is closed first: its mutex tells Setup and
; Uninstall it is there, [Code] asks it to quit, and Restart Manager closes whatever else holds the files
AppMutex={#AppMutexName}
SetupMutex=SimplCourses.Simpl.Setup
CloseApplications=yes
RestartApplications=no

; Settings > Apps
UninstallDisplayName=Simpl
UninstallDisplayIcon={app}\Simpl.exe

OutputDir={#OutputDir}
OutputBaseFilename=Simpl-Setup
Compression=lzma2/max
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WizardSelectTasks=Install Simpl
SelectTasksDesc=For you on this PC. No administrator needed.
SelectTasksLabel2=Simpl goes in your Start menu. Click Install to begin.
FinishedHeadingLabel=Simpl is installed
FinishedLabelNoIcons=Simpl is on this PC.
FinishedLabel=Find Simpl in your Start menu. Your settings and sign-in are kept apart from the app, so they stay through updates.
ClickFinish=Click Finish to close Setup.
; (%1: Simpl for the AppMutex check, "Setup" for the SetupMutex one -- another Setup already running -- as Setup
; fills it in for each)
SetupAppRunningError=%1 is still open. Close it, then click OK. Or click Cancel to stop.%n%nIf it is Simpl, it may be in the notification area, by the clock: right-click its icon there and choose Quit Simpl.
UninstallAppRunningError=%1 is still open. It may be in the notification area, by the clock.%n%nRight-click its icon there and choose Quit Simpl, then click OK. Or click Cancel to stop.
ConfirmUninstall=Uninstall Simpl from this PC?%n%nYour settings, grade history and saved sign-in are kept (in AppData\Local\Simpl and Windows' Credential Manager), in case you install Simpl again. To erase them too, first open Simpl's Settings > Data > Reset everything.
UninstalledAll=Simpl was removed from this PC.%n%nYour settings are still in AppData\Local\Simpl, and a saved sign-in in Windows' Credential Manager (Simpl Courses/sign-in). Delete them if you no longer want them.

[CustomMessages]
SimplIsOpen=Simpl is open, so Setup will close it first.%n%nIt opens again at the end if you leave Open Simpl ticked.

[Tasks]
Name: "desktopicon"; Description: "Put a shortcut on the &desktop"; GroupDescription: "Shortcuts:"; Flags: unchecked

[InstallDelete]
; (an earlier version's own folders, so nothing it had and this one has not is left to be read)
Type: filesandordirs; Name: "{app}\extension"
Type: filesandordirs; Name: "{app}\Web"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; (both carry Simpl's app id: its windows, its taskbar button and these are one app, pinned as one)
Name: "{autoprograms}\Simpl"; Filename: "{app}\Simpl.exe"; WorkingDir: "{app}"; AppUserModelID: "{#AppUserModelID}"; Comment: "Your courses, as a Windows app"
Name: "{autodesktop}\Simpl"; Filename: "{app}\Simpl.exe"; WorkingDir: "{app}"; AppUserModelID: "{#AppUserModelID}"; Comment: "Your courses, as a Windows app"; Tasks: desktopicon

[Registry]
; (the name and picture Windows puts on Simpl's notifications, for its app id; gone with the uninstall)
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelID}"; ValueType: string; ValueName: "DisplayName"; ValueData: "Simpl"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\{#AppUserModelID}"; ValueType: string; ValueName: "IconUri"; ValueData: "{app}\Assets\Simpl-256.png"

[Run]
; the last page's Open Simpl (ticked)
Filename: "{app}\Simpl.exe"; WorkingDir: "{app}"; Description: "Open Simpl"; Flags: nowait postinstall skipifsilent
; an update Simpl ran (/LAUNCH): Simpl opened again once the new version is in place
Filename: "{app}\Simpl.exe"; WorkingDir: "{app}"; Flags: nowait; Check: LaunchAfterUpdate

[Code]
const
  EVENT_MODIFY_STATE = $0002;

var
  { the new version put in place (ssPostInstall): an update that never got there opens the old one again }
  InstallDone: Boolean;

{ kernel32, to ask an open Simpl to quit. (Setup is a 32-bit program, as Inno Setup makes it by default: a handle fits
  in an Integer, and a BOOL is one.) }
function SimplOpenEvent(DesiredAccess: Cardinal; InheritHandle: Integer; Name: String): Integer;
  external 'OpenEventW@kernel32.dll stdcall';
function SimplSetEvent(Handle: Integer): Integer;
  external 'SetEvent@kernel32.dll stdcall';
function SimplCloseHandle(Handle: Integer): Integer;
  external 'CloseHandle@kernel32.dll stdcall';

function CommandLineHas(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
      Result := True;
end;

{ Run by Simpl to update itself (Restart to Update): Simpl is quitting already, and is opened again at the end. }
function UpdatingFromSimpl: Boolean;
begin
  Result := CommandLineHas('/LAUNCH');
end;

function LaunchAfterUpdate: Boolean;
begin
  Result := WizardSilent and UpdatingFromSimpl;
end;

{ A Simpl is open: every copy holds this mutex while it runs (Program.cs, Instance.Claim). }
function SimplRunning: Boolean;
begin
  Result := CheckForMutexes('{#AppMutexName}');
end;

{ Simpl asked to quit -- it listens for this event and quits as its own Quit Simpl does -- and given until Timeout
  (ms) to have gone. Each open copy takes one signal, so it is given again each second while one is left. True once
  none is open. }
function CloseSimpl(Timeout: Integer): Boolean;
var
  Waited, QuitEvent: Integer;
begin
  Waited := 0;
  while SimplRunning and (Waited < Timeout) do
  begin
    if Waited mod 1000 = 0 then
    begin
      QuitEvent := SimplOpenEvent(EVENT_MODIFY_STATE, 0, '{#QuitEventName}');
      if QuitEvent <> 0 then
      begin
        SimplSetEvent(QuitEvent);
        SimplCloseHandle(QuitEvent);
      end;
    end;
    Sleep(250);
    Waited := Waited + 250;
  end;
  Result := not SimplRunning;
  if Waited = 0 then
    Exit;
  if Result then
  begin
    Log('Simpl was open: asked to quit, it had gone after ' + IntToStr(Waited) + ' ms.');
    { (its web view's helper processes let go of their files a moment after it) }
    Sleep(1500);
  end
  else
    Log('Simpl was open: asked to quit, it was still open after ' + IntToStr(Waited) + ' ms.');
end;

{ Before Setup's own check that Simpl is not open: someone who opened Setup themselves is asked first; an update or a
  silent install closes it. Still open after that: Setup's check says so, with OK to try again. }
function InitializeSetup: Boolean;
begin
  Result := True;
  if not SimplRunning then
    Exit;
  if not WizardSilent and not UpdatingFromSimpl then
    if SuppressibleMsgBox(CustomMessage('SimplIsOpen'), mbConfirmation, MB_OKCANCEL, IDOK) <> IDOK then
    begin
      Result := False;
      Exit;
    end;
  CloseSimpl(30000);
end;

{ The desktop shortcut's page is the last before installing (the "ready" page is off): its button says Install. }
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpSelectTasks then
    WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);
end;

{ The new version is in place: ssPostInstall comes only after a successful install (and after the [Run] entries, which
  also run only then). }
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallDone := True;
end;

{ An update Simpl ran (Restart to Update) that could not put the new version in place -- another Simpl that would not
  quit (Setup's check answered Cancel, as /SUPPRESSMSGBOXES does), another Setup running, a file it could not replace:
  Simpl has quit already and [Run] never comes, so the version that was there is opened again rather than left closed.
  (Called whenever Setup ends, even before anything is installed.) }
procedure DeinitializeSetup;
var
  Dir, Exe: String;
  ResultCode: Integer;
begin
  if InstallDone or not WizardSilent or not UpdatingFromSimpl then
    Exit;
  { (the installed copy's entry in Settings > Apps, named by AppId) }
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B65F70B7-E13E-4CCC-9E1C-0B659660D130}_is1',
    'InstallLocation', Dir) then
  begin
    Log('The update did not finish, and no installed Simpl was found to open again.');
    Exit;
  end;
  Exe := AddBackslash(Dir) + 'Simpl.exe';
  if not FileExists(Exe) then
  begin
    Log('The update did not finish, and ' + Exe + ' is not there to open again.');
    Exit;
  end;
  Log('The update did not finish: opening the copy before it again (' + Exe + ').');
  Exec(Exe, '', Dir, SW_SHOWNORMAL, ewNoWait, ResultCode);
end;

{ Uninstalling, once it is confirmed: an open Simpl is closed before Uninstall's own check that it is not. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usAppMutexCheck then
    CloseSimpl(15000);
end;
