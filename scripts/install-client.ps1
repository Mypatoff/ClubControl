#Requires -Version 5.1
<#
    Run once on each gaming PC, from the folder containing the published ClubControl.Client.exe
    (e.g. after "dotnet publish -c Release -r win-x64 --self-contained false").

    What it does (SPEC section 1):
      - Creates a Task Scheduler task that starts ClubControl.Client.exe at user logon, running
        with the highest available privileges (needed for the keyboard-blocking hook and shutdown).

    Set up Windows auto-logon separately first (see README.md) so the task actually runs without
    anyone typing a password at the keyboard.

    Usage:
      powershell -ExecutionPolicy Bypass -File install-client.ps1
#>

param(
    [string]$ExePath = (Join-Path $PSScriptRoot "ClubControl.Client.exe"),
    [string]$TaskName = "ClubControl Client"
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Host "Elevation required - relaunching as Administrator..."
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ExePath `"$ExePath`" -TaskName `"$TaskName`""
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs
    exit
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Could not find $ExePath. Pass -ExePath, or run this script from the publish folder."
    exit 1
}

Write-Host "Using exe: $ExePath"

schtasks /Create /TN "$TaskName" /TR "`"$ExePath`"" /SC ONLOGON /RL HIGHEST /F | Out-Null

Write-Host "Created scheduled task '$TaskName': starts at logon, highest privileges."
Write-Host ""
Write-Host "Done. See README.md for the one-time per-PC setup (Wake-on-LAN, Fast Startup, auto-logon)."
