#Requires -Version 5.1
<#
    Run once on each gaming PC, from the folder containing the published ClubControl.Client.exe
    (e.g. after "dotnet publish -c Release -r win-x64 --self-contained false").

    What it does (SPEC section 1, extended for always-on reliability):
      - Creates a Task Scheduler task that starts ClubControl.Client.exe at logon for the given
        user, running with the highest available privileges (needed for the keyboard-blocking
        hook and shutdown/restart).
      - Configures it to restart automatically (up to 999 times, 1 minute apart) if the process
        ever exits unexpectedly, and removes the default 3-day execution time limit - this task is
        meant to run indefinitely, not be killed after 72 hours.

    IMPORTANT: -UserId must be the account that will actually be auto-logged-on on this PC (see
    README.md), not necessarily whichever account you're running this install script as. If you
    had to use a temporary admin account just to run this elevated, pass the real kiosk account
    explicitly, e.g.: install-client.ps1 -UserId ClubPC1

    Set up Windows auto-logon separately first (see README.md) so the task actually runs without
    anyone typing a password at the keyboard.

    Usage:
      powershell -ExecutionPolicy Bypass -File install-client.ps1
      powershell -ExecutionPolicy Bypass -File install-client.ps1 -UserId ClubPC1
#>

param(
    [string]$ExePath = (Join-Path $PSScriptRoot "ClubControl.Client.exe"),
    [string]$TaskName = "ClubControl Client",
    [string]$UserId = $env:USERNAME
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Host "Elevation required - relaunching as Administrator..."
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ExePath `"$ExePath`" -TaskName `"$TaskName`" -UserId `"$UserId`""
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs
    exit
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Could not find $ExePath. Pass -ExePath, or run this script from the publish folder."
    exit 1
}

Write-Host "Using exe: $ExePath"
Write-Host "Task will run as user: $UserId"

$action = New-ScheduledTaskAction -Execute $ExePath
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $UserId
$principal = New-ScheduledTaskPrincipal -UserId $UserId -RunLevel Highest -LogonType Interactive
$settings = New-ScheduledTaskSettingsSet `
    -RestartCount 999 `
    -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger `
    -Principal $principal -Settings $settings -Force | Out-Null

Write-Host "Created scheduled task '$TaskName': starts at logon for '$UserId', highest privileges,"
Write-Host "restarts automatically (up to 999 times, 1 minute apart) if it ever exits, no 72-hour limit."
Write-Host ""
Write-Host "Done. See README.md for the one-time per-PC setup (Wake-on-LAN, Fast Startup, auto-logon)."
