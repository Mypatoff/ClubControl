using System;
using Microsoft.Win32;

namespace ClubControl.Server.Native;

/// <summary>Starts ClubControl with Windows via the per-user registry Run key (SPEC section 1). No elevation needed.</summary>
public static class AutoStart
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClubControl";

    public static void Ensure()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);
        key?.SetValue(ValueName, $"\"{exePath}\"");
    }
}
