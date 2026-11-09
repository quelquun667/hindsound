; Hindsound's per-user installer, for Inno Setup 7.1. The exact commands and the trials are in installer/README.md.
;   ISCC.exe /DMyAppVersion=X.Y.Z /DPublishDir=<folder of dotnet publish> /O<output folder> installer\Hindsound.iss
;
; The app starts this Setup and then leaves:
;   Hindsound-X.Y.Z-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /WAITEXIT=330 /RESTARTAPP
;   /WAITEXIT=N   waits up to N seconds for the app's mutex to go, instead of failing on the race. N must outlast the app's
;                 slowest exit, 5 min for a clip being written and 5 s for the engine: an app that leaves after Setup gave up
;                 stays off, because nothing is left to start it again.
;   /RESTARTAPP   starts the app again at the end: the new version, or the one in place if the installation failed.
;                 That one gets --update-failed on its command line.
; Without /RESTARTAPP (the app was left with Quit), nothing is started, even after a failure.

#ifndef MyAppVersion
  #error Pass the version: /DMyAppVersion=X.Y.Z
#endif
#ifndef PublishDir
  #error Pass the folder of dotnet publish: /DPublishDir=<folder>
#endif

#define AppGuid "5F653819-2FB1-45C5-96CC-AE1C0188B054"
#define RepoRoot SourcePath + ".."

; /DTrialRoot=<folder> is for the trials of installer/README.md and is never passed for a release. It keeps a trial away
; from the real data, Run key and instance of whoever runs it, and keeps the started app on a safe settings file.
; An uninstaller from a build without it deletes the real log and the real Run value: never run one on a working PC.
#ifdef TrialRoot
  #define AppMutexName "Local\HindsoundTrial.SingleInstance"
  #define AppArguments "--settings " + TrialRoot + "\settings.json --offscreen --exit-after 90"
  #define LocalDataDir TrialRoot + "\local"
  #define RoamingDataDir TrialRoot + "\roaming"
  #define RunKey "Software\Hindsound-InstallerTrial\Run"
#else
  #define AppMutexName "Local\Hindsound.SingleInstance"
  #define AppArguments ""
  #define LocalDataDir "{localappdata}\Hindsound"
  #define RoamingDataDir "{userappdata}\Hindsound"
  #define RunKey "Software\Microsoft\Windows\CurrentVersion\Run"
#endif

[Setup]
AppId={{{#AppGuid}}
AppName=Hindsound
AppVersion={#MyAppVersion}
AppVerName=Hindsound {#MyAppVersion}
AppPublisher=quelquun667
AppPublisherURL=https://github.com/quelquun667/hindsound
VersionInfoVersion={#MyAppVersion}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; With lowest privileges, this is %LocalAppData%\Programs\Hindsound, never %LocalAppData%\Hindsound and its Rescued folder.
DefaultDirName={autopf}\Hindsound
DefaultGroupName=Hindsound
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
; Restart Manager cannot close an app without a window (trial of 06/11/2026: 30 s, then code 5). The mutex can tell.
AppMutex={#AppMutexName}
CloseApplications=no
OutputBaseFilename=Hindsound-{#MyAppVersion}-Setup
SetupIconFile={#RepoRoot}\src\Hindsound\Assets\icon.ico
UninstallDisplayName=Hindsound
UninstallDisplayIcon={app}\Hindsound.exe
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern

[Files]
; In this order on purpose. A rollback removes new files but does not bring back the ones it replaced (trial of 06/11/2026:
; DLL locked, exe already new, version in the Uninstall key still old). With the exe last, any failure leaves the old exe.
; The names and the licenses folder stay as in the repo, because THIRD-PARTY-NOTICES.md links to them.
; A missing file stops the compilation: a Setup never ships without its licences.
Source: "{#RepoRoot}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepoRoot}\licenses\*.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "{#PublishDir}\libmp3lame.64.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\Hindsound.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Hindsound"; Filename: "{app}\Hindsound.exe"

[Registry]
; The app owns this value (Settings/StartupRegistration.cs): Setup never writes it, the uninstaller only removes it.
Root: HKCU; Subkey: "{#RunKey}"; ValueType: none; ValueName: "Hindsound"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\Hindsound.exe"; Parameters: "{#AppArguments}"; Description: "{cm:LaunchProgram,Hindsound}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\Hindsound.exe"; Parameters: "{#AppArguments}"; Flags: nowait; Check: RestartRequested

[UninstallDelete]
; Clips and Rescued stay: they are the user's.
Type: files; Name: "{#LocalDataDir}\hindsound.log"
Type: filesandordirs; Name: "{#LocalDataDir}\updates"
Type: dirifempty; Name: "{#LocalDataDir}"

[Code]
var
  InstallFinished: Boolean;
  ExeInPlace: String;

function RestartRequested: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/RESTARTAPP') = 0 then
      Result := True;
end;

// After a failure, the app comes back only if it asked to, and if it has not stayed or come back by itself.
// The mutex at the start of Setup cannot say whether the app was running: the app may already be gone, and Quit must not
// bring it back. A first installation has no exe in place.
function MustRestartInPlace(Asked, Finished, AppIsRunning: Boolean; const Exe: String): Boolean;
begin
  Result := Asked and (not Finished) and (not AppIsRunning) and (Exe <> '');
end;

function InitializeSetup: Boolean;
var
  Waited, Limit: Integer;
  Folder: String;
begin
  // {app} is not known yet if Setup stops early, so the exe in place is read here.
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppGuid}' + '}_is1', 'InstallLocation', Folder) then
    ExeInPlace := AddBackslash(Folder) + 'Hindsound.exe';

  Limit := StrToIntDef(ExpandConstant('{param:WAITEXIT|0}'), 0) * 5;
  Waited := 0;
  while (Waited < Limit) and CheckForMutexes('{#AppMutexName}') do
  begin
    Sleep(200);
    Waited := Waited + 1;
  end;
  Log(Format('Hindsound: waited %d ms for the app to leave', [Waited * 200]));

  // If the app is still there, AppMutex now refuses the installation.
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    InstallFinished := True;
end;

procedure DeinitializeSetup;
var
  Code: Integer;
begin
  if MustRestartInPlace(RestartRequested, InstallFinished, CheckForMutexes('{#AppMutexName}'), ExeInPlace) then
  begin
    if Exec(ExeInPlace, '{#AppArguments} --update-failed', ExtractFileDir(ExeInPlace), SW_SHOWNORMAL, ewNoWait, Code) then
      Log('Hindsound: installation failed, the version in place was started again: ' + ExeInPlace)
    else
      Log('Hindsound: installation failed, and the version in place could not start: ' + SysErrorMessage(Code));
  end
  else
    Log('Hindsound: nothing to start again');
end;

function DeleteSettingsWanted: Boolean;
begin
#ifdef TrialDeleteSettings
  Result := True;
#else
  // A silent uninstallation asks nothing and keeps the settings and the collections.
  Result := (not UninstallSilent) and
    (MsgBox('Also delete your Hindsound settings and collections?' #13#10 'Your clips are kept either way.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES);
#endif
end;

// By name, not the whole folder: the clips folder can be set to any folder, this one included, and the question says clips stay.
// settings.json, its .bak copies and a .tmp left by a crash all start with this name, and so does library.json, which holds the
// collections, with its own. The folder goes only if nothing else is in it.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Folder: String;
begin
  if (CurUninstallStep = usPostUninstall) and DeleteSettingsWanted then
  begin
    Folder := ExpandConstant('{#RoamingDataDir}');
    DelTree(Folder + '\settings.json*', False, True, False);
    DelTree(Folder + '\library.json*', False, True, False);
    RemoveDir(Folder);
  end;
end;
