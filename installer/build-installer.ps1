<#
    Builds dist\ClubControl-Setup.exe: publishes the server and the client self-contained (so the
    PCs need no separate .NET install), then compiles installer\ClubControl.iss with Inno Setup.

    Usage (from anywhere):
      powershell -File installer\build-installer.ps1
      powershell -File installer\build-installer.ps1 -Version 1.2.0
#>
param(
    [string]$Version = "1.0.0",
    [string]$InnoCompiler = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $InnoCompiler) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path $InnoCompiler)) {
    throw "Inno Setup 6 not found. Install it from https://jrsoftware.org/isdl.php or pass -InnoCompiler <path to ISCC.exe>."
}

Remove-Item -Recurse -Force publish -ErrorAction SilentlyContinue
dotnet publish src/ClubControl.Server/ClubControl.Server.csproj -c Release -r win-x64 --self-contained true -o publish/server
if ($LASTEXITCODE -ne 0) { throw "Server publish failed." }
dotnet publish src/ClubControl.Client/ClubControl.Client.csproj -c Release -r win-x64 --self-contained true -o publish/client
if ($LASTEXITCODE -ne 0) { throw "Client publish failed." }

& $InnoCompiler "/DAppVersion=$Version" "installer\ClubControl.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed." }

Write-Host ""
Write-Host "Done: $root\dist\ClubControl-Setup.exe"
