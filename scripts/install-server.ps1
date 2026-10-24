#Requires -Version 5.1
<#
    Run once on the admin laptop, from the folder containing the published ClubControl.Server.exe
    (e.g. after "dotnet publish -c Release -r win-x64 --self-contained false").

    What it does (SPEC sections 1 and 2):
      - Adds Windows Firewall inbound allow rules for TCP 5050 and UDP 5051, Private profile only.
        Never disables the firewall or touches any other rule.
      - Registers ClubControl.Server.exe to start automatically for the current Windows user
        (HKCU Run key) - no Task Scheduler needed, the app itself also sets this on every run.

    Usage:
      powershell -ExecutionPolicy Bypass -File install-server.ps1
#>

param(
    [string]$ExePath = (Join-Path $PSScriptRoot "ClubControl.Server.exe")
)

$ErrorActionPreference = "Stop"

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Host "Elevation required - relaunching as Administrator..."
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -ExePath `"$ExePath`""
    Start-Process -FilePath "powershell.exe" -ArgumentList $arguments -Verb RunAs
    exit
}

if (-not (Test-Path $ExePath)) {
    Write-Error "Could not find $ExePath. Pass -ExePath, or run this script from the publish folder."
    exit 1
}

Write-Host "Using exe: $ExePath"

# ---- Firewall rules (Private profile only, inbound allow - never disables the firewall) ----

function Ensure-FirewallRule {
    param([string]$DisplayName, [string]$Protocol, [int]$Port)

    $existing = Get-NetFirewallRule -DisplayName $DisplayName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Firewall rule '$DisplayName' already exists - skipping."
        return
    }

    New-NetFirewallRule -DisplayName $DisplayName -Direction Inbound -Action Allow `
        -Protocol $Protocol -LocalPort $Port -Profile Private | Out-Null
    Write-Host "Added firewall rule '$DisplayName' ($Protocol $Port, Private profile)."
}

Ensure-FirewallRule -DisplayName "ClubControl Server (TCP 5050)" -Protocol TCP -Port 5050
Ensure-FirewallRule -DisplayName "ClubControl Discovery (UDP 5051)" -Protocol UDP -Port 5051

# ---- Autostart (HKCU Run key) ----

$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
Set-ItemProperty -Path $runKeyPath -Name "ClubControl" -Value "`"$ExePath`""
Write-Host "Registered ClubControl to start automatically for this Windows user."

Write-Host ""
Write-Host "Done. See README.md for the one-time per-PC setup (Wake-on-LAN, Fast Startup, auto-logon)."
