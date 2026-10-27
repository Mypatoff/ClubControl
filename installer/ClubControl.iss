; ClubControl setup: one installer, the admin picks "Server" (admin PC) or "Client" (gaming PC).
; Build it with installer\build-installer.ps1 (publishes both apps first), or by hand with
;   ISCC.exe /DAppVersion=1.0.0 installer\ClubControl.iss
; Both apps are published self-contained, so nothing else (.NET) has to be installed on the PC.

#define AppName "ClubControl"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef ServerDir
  #define ServerDir "..\publish\server"
#endif
#ifndef ClientDir
  #define ClientDir "..\publish\client"
#endif

[Setup]
AppId={{7D0C6A52-3B1E-4F8A-9C4D-5E2B8A1F6C93}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\dist
OutputBaseFilename=ClubControl-Setup
SetupIconFile=..\assets\ClubControl.ico
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\Server\ClubControl.Server.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The server opens firewall ports and the client registers a high-privilege startup task: both need admin.
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.TypeServer=Server - the admin PC (admin panel, billing, reports)
english.TypeClient=Client - a gaming PC (lock screen and timer)
english.CompServer=ClubControl Server
english.CompClient=ClubControl Client
english.Desktop=Create a &desktop shortcut
english.LaunchServer=Start ClubControl Server now
english.KioskTitle=Windows account of this gaming PC
english.KioskDescription=The client starts automatically when this account logs on.
english.KioskLabel=Account that logs on automatically (see "Configure Windows auto-logon" in the README):
english.KioskEmpty=Please enter the Windows account name.
english.TaskFailed=The client could not be set to start automatically (error %1). Create the task by hand with scripts\install-client.ps1.
russian.TypeServer=Сервер - компьютер администратора (панель, оплата, отчёты)
russian.TypeClient=Клиент - игровой компьютер (экран блокировки и таймер)
russian.CompServer=ClubControl Сервер
russian.CompClient=ClubControl Клиент
russian.Desktop=Создать ярлык на &рабочем столе
russian.LaunchServer=Запустить ClubControl Сервер
russian.KioskTitle=Учётная запись Windows этого игрового компьютера
russian.KioskDescription=Клиент запускается автоматически, когда эта учётная запись входит в систему.
russian.KioskLabel=Учётная запись с автоматическим входом (см. «Настройка автовхода Windows» в README):
russian.KioskEmpty=Введите имя учётной записи Windows.
russian.TaskFailed=Не удалось настроить автозапуск клиента (ошибка %1). Создайте задачу вручную: scripts\install-client.ps1.

[Types]
Name: "server"; Description: "{cm:TypeServer}"
Name: "client"; Description: "{cm:TypeClient}"

[Components]
Name: "server"; Description: "{cm:CompServer}"; Types: server; Flags: fixed
Name: "client"; Description: "{cm:CompClient}"; Types: client; Flags: fixed

[Tasks]
Name: "desktopicon"; Description: "{cm:Desktop}"; Components: server

[Files]
Source: "{#ServerDir}\*"; DestDir: "{app}\Server"; Components: server; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#ClientDir}\*"; DestDir: "{app}\Client"; Components: client; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ClubControl Server"; Filename: "{app}\Server\ClubControl.Server.exe"; Components: server
Name: "{group}\Uninstall ClubControl"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClubControl Server"; Filename: "{app}\Server\ClubControl.Server.exe"; Tasks: desktopicon; Components: server

[Run]
; Started as the user who ran setup (not the elevated process), so the server's own "start at logon" entry lands in the right profile.
Filename: "{app}\Server\ClubControl.Server.exe"; Description: "{cm:LaunchServer}"; Flags: nowait postinstall skipifsilent runasoriginaluser; Components: server

[Code]
const
  ClientTaskName = 'ClubControl Client';
  TcpRuleName = 'ClubControl Server (TCP 5050)';
  UdpRuleName = 'ClubControl Discovery (UDP 5051)';

var
  KioskPage: TInputQueryWizardPage;

function RunHidden(const FileName, Params: String): Integer;
var
  ResultCode: Integer;
begin
  if Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Result := ResultCode
  else
    Result := -1;
end;

procedure InitializeWizard;
begin
  KioskPage := CreateInputQueryPage(wpSelectComponents, CustomMessage('KioskTitle'),
    CustomMessage('KioskDescription'), CustomMessage('KioskLabel'));
  KioskPage.Add('', False);
  // /KioskUser=name pre-fills (and with /VERYSILENT supplies) the account, for unattended installs.
  KioskPage.Values[0] := ExpandConstant('{param:KioskUser|}');
  if KioskPage.Values[0] = '' then
    KioskPage.Values[0] := GetUserNameString;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = KioskPage.ID) and not WizardIsComponentSelected('client');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = KioskPage.ID) and (Trim(KioskPage.Values[0]) = '') then
  begin
    MsgBox(CustomMessage('KioskEmpty'), mbError, MB_OK);
    Result := False;
  end;
end;

procedure StopRunningApps;
var
  Dir: String;
begin
  RunHidden(ExpandConstant('{sys}\schtasks.exe'), '/End /TN "' + ClientTaskName + '"');

  // Only processes running from the install folder - never a copy started from somewhere else.
  Dir := ExpandConstant('{app}');
  StringChangeEx(Dir, '''', '''''', True);
  RunHidden(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "Get-Process -Name ''ClubControl.Client'',''ClubControl.Server'' -ErrorAction SilentlyContinue | ' +
    'Where-Object { $_.Path -like ''' + Dir + '\*'' } | Stop-Process -Force"');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  // An update must be able to overwrite the running programs.
  StopRunningApps;
  Result := '';
end;

procedure AddFirewallRules;
var
  Netsh: String;
begin
  // Inbound allow on the Private profile only - never touches the firewall switch or any other rule.
  Netsh := ExpandConstant('{sys}\netsh.exe');
  RunHidden(Netsh, 'advfirewall firewall delete rule name="' + TcpRuleName + '"');
  RunHidden(Netsh, 'advfirewall firewall delete rule name="' + UdpRuleName + '"');
  RunHidden(Netsh, 'advfirewall firewall add rule name="' + TcpRuleName + '" dir=in action=allow protocol=TCP localport=5050 profile=private');
  RunHidden(Netsh, 'advfirewall firewall add rule name="' + UdpRuleName + '" dir=in action=allow protocol=UDP localport=5051 profile=private');
end;

procedure CreateClientTask;
var
  User, Exe, Script: String;
  Code: Integer;
begin
  // Same task as scripts\install-client.ps1: starts at logon for the kiosk account with the highest
  // privileges, restarts itself if it ever exits, has no 72-hour limit, and also re-checks every
  // 1 minute regardless of why the client isn't running - Task Scheduler's own restart-on-failure
  // does NOT fire if someone manually ends the process (Task Manager "End Task" counts as a
  // deliberate stop, not a failure). The client's single-instance mutex makes the 1-minute recheck
  // a harmless no-op if it's already running.
  User := Trim(KioskPage.Values[0]);
  StringChangeEx(User, '''', '''''', True);
  Exe := ExpandConstant('{app}\Client\ClubControl.Client.exe');
  StringChangeEx(Exe, '''', '''''', True);

  Script := '$a = New-ScheduledTaskAction -Execute ''' + Exe + '''; ' +
    '$t = New-ScheduledTaskTrigger -AtLogOn -User ''' + User + '''; ' +
    '$r = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 1) ' +
    '-RepetitionDuration (New-TimeSpan -Days 3650); $t.Repetition = $r.Repetition; ' +
    '$p = New-ScheduledTaskPrincipal -UserId ''' + User + ''' -RunLevel Highest -LogonType Interactive; ' +
    '$s = New-ScheduledTaskSettingsSet -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) ' +
    '-ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable; ' +
    'Register-ScheduledTask -TaskName ''' + ClientTaskName + ''' -Action $a -Trigger $t -Principal $p -Settings $s -Force | Out-Null';

  Code := RunHidden(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "' + Script + '"');
  if Code <> 0 then
    MsgBox(FmtMessage(CustomMessage('TaskFailed'), [IntToStr(Code)]), mbError, MB_OK)
  else if ExpandConstant('{param:NoStart|0}') <> '1' then
    // Starts it right away if that account is logged on; otherwise it starts at its next logon.
    RunHidden(ExpandConstant('{sys}\schtasks.exe'), '/Run /TN "' + ClientTaskName + '"');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if WizardIsComponentSelected('server') then
      AddFirewallRules;
    if WizardIsComponentSelected('client') then
      CreateClientTask;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    // Stops the programs and removes what setup added. %ProgramData%\ClubControl (the database,
    // backups, logs) is deliberately left alone.
    StopRunningApps;
    if FileExists(ExpandConstant('{app}\Client\ClubControl.Client.exe')) then
      RunHidden(ExpandConstant('{sys}\schtasks.exe'), '/Delete /TN "' + ClientTaskName + '" /F');
    // The firewall rules belong to the server install only.
    if FileExists(ExpandConstant('{app}\Server\ClubControl.Server.exe')) then
    begin
      RunHidden(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="' + TcpRuleName + '"');
      RunHidden(ExpandConstant('{sys}\netsh.exe'), 'advfirewall firewall delete rule name="' + UdpRuleName + '"');
    end;
  end;
end;
