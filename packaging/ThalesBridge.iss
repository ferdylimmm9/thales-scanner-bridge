; Build with Inno Setup 7.1.1 or newer. Binaries are published before compilation.
#if Ver < EncodeVer(7, 1, 1)
  #error Inno Setup 7.1.1 or newer is required
#endif
#ifndef ReleaseVersion
  #define ReleaseVersion "0.0.0-dev"
#endif
#ifndef FileVersion
  #define FileVersion "0.0.0.0"
#endif
[Setup]
SetupArchitecture=x64
AppId=ThalesScannerBridge
AppName=Thales Scanner Bridge
AppVersion={#ReleaseVersion}
VersionInfoVersion={#FileVersion}
AppPublisher=Solaire Resort & Casino
DefaultDirName={autopf}\ThalesBridge
DisableDirPage=yes
UsePreviousAppDir=no
DefaultGroupName=Thales Scanner Bridge
DisableProgramGroupPage=yes
PrivilegesRequired=admin
SetupMutex=ThalesScannerBridgeSetup
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
SourceDir=..
OutputDir=.
OutputBaseFilename=ThalesBridgeSetup
Compression=lzma2/ultra64
SolidCompression=yes
SetupLogging=yes
UninstallLogging=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\ThalesBridgeManager.exe
WizardStyle=modern
#ifdef SigningTool
SignTool={#SigningTool}
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: checkedonce

[Files]
Source: "packaging\maintain.ps1"; Flags: dontcopy
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "setup.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "uninstall.ps1"; DestDir: "{app}"; Flags: ignoreversion
Source: "packaging\maintain.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Scanner Manager"; Filename: "{app}\ThalesBridgeManager.exe"
Name: "{group}\Uninstall Thales Scanner Bridge"; Filename: "{uninstallexe}"
Name: "{commondesktop}\Thales Scanner Bridge"; Filename: "{app}\ThalesBridgeManager.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ThalesBridgeManager.exe"; Description: "Open Scanner Manager"; Flags: postinstall nowait skipifsilent shellexec runasoriginaluser

[UninstallDelete]
; Only generated files owned by this product. Never delete arbitrary user logs or SDK files.
Type: files; Name: "{app}\bridge-settings.json"
Type: files; Name: "{app}\run-bridge.cmd"

[Code]
var
  SdkPage: TInputFileWizardPage;
  PatchPage: TInputOptionWizardPage;
  Prepared, Configured: Boolean;

function PowerShell: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function Quote(Value: String): String;
begin
  Result := '"' + Value + '"';
end;

function InvokeMaintenance(Script, Action, Extra: String): Boolean;
var ExitCode: Integer;
begin
  Result := Exec(PowerShell, '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    Quote(Script) + ' -Action ' + Action + Extra, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Log(Format('Maintenance %s exit code: %d', [Action, ExitCode]));
  Result := Result and (ExitCode = 0);
end;

function ExtraOptions: String;
begin
  Result := '';
  if SdkPage.Values[0] <> '' then Result := Result + ' -SdkMsi ' + Quote(SdkPage.Values[0]);
  if not PatchPage.Values[0] then Result := Result + ' -SkipUvIrPatch';
end;

// {app} is not initialized until installation starts; the wizard already holds the fixed directory.
function ExistingUninstaller: String;
begin
  Result := AddBackslash(WizardDirValue) + 'unins000.exe';
end;

procedure OpenExistingUninstaller(Sender: TObject);
var ExitCode: Integer;
begin
  if MsgBox('Uninstall the existing Thales Scanner Bridge? Diagnostic logs and the Thales SDK will be kept.', mbConfirmation, MB_YESNO) <> IDYES then Exit;
  if Exec(ExistingUninstaller, '', '', SW_SHOW, ewWaitUntilTerminated, ExitCode) and (ExitCode = 0) then
    WizardForm.Close
  else
    MsgBox('Uninstall could not complete. Use Windows Installed apps and review its log.', mbError, MB_OK);
end;

procedure InitializeWizard;
var UninstallButton: TNewButton;
begin
  SdkPage := CreateInputFilePage(wpWelcome, 'Thales SDK prerequisite',
    'Select the licensed Thales SDK MSI if it is not already installed.',
    'Leave this blank when the SDK is installed. The SDK is not bundled.');
  SdkPage.Add('Thales SDK MSI:', 'Windows Installer (*.msi)|*.msi', '.msi');
  SdkPage.Values[0] := ExpandConstant('{param:SDKMSI|}');
  PatchPage := CreateInputOptionPage(SdkPage.ID, 'Reader settings',
    'Select the option for your reader model.',
    'QS2000 requires visible-light capture. Other models may support UV/IR.', False, False);
  PatchPage.Add('Disable UV/IR capture (QS2000)');
  PatchPage.Values[0] := GetPreviousData('DisableUvIr', '1') = '1';
  if FileExists(ExistingUninstaller) then begin
    UninstallButton := TNewButton.Create(WizardForm);
    UninstallButton.Parent := WizardForm;
    UninstallButton.Caption := 'Uninstall existing';
    UninstallButton.SetBounds(ScaleX(12), WizardForm.CancelButton.Top, ScaleX(130), WizardForm.CancelButton.Height);
    UninstallButton.OnClick := @OpenExistingUninstaller;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  if PatchPage.Values[0] then SetPreviousData(PreviousDataKey, 'DisableUvIr', '1')
  else SetPreviousData(PreviousDataKey, 'DisableUvIr', '0');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Prepared then Exit;
  ExtractTemporaryFile('maintain.ps1');
  if not InvokeMaintenance(ExpandConstant('{tmp}\maintain.ps1'), 'Prepare', ExtraOptions) then
    Result := 'Prerequisite or shutdown check failed. See C:\ProgramData\ThalesBridge\InstallerLogs.'
  else Prepared := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    if not InvokeMaintenance(ExpandConstant('{app}\maintain.ps1'), 'Configure', ExtraOptions) then
      RaiseException('Bridge configuration failed. Review C:\ProgramData\ThalesBridge\InstallerLogs, then rerun setup to repair.');
    Configured := True;
  end;
end;

procedure DeinitializeSetup;
begin
  if Prepared and not Configured then
    InvokeMaintenance(ExpandConstant('{tmp}\maintain.ps1'), 'Resume', '');
end;

function InitializeUninstall: Boolean;
begin
  Result := InvokeMaintenance(ExpandConstant('{app}\maintain.ps1'), 'Remove', '');
  if not Result then
    MsgBox('Bridge cleanup failed. Uninstall stopped before removing files. Review C:\ProgramData\ThalesBridge\InstallerLogs.', mbError, MB_OK);
end;
