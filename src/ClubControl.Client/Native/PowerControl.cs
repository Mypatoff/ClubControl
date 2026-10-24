using System.Diagnostics;

namespace ClubControl.Client.Native;

/// <summary>Runs the built-in Windows shutdown.exe. No registry/policy changes involved.</summary>
public static class PowerControl
{
    public static void Shutdown() => Run("/s /t 0");

    public static void Restart() => Run("/r /t 0");

    private static void Run(string arguments) =>
        Process.Start(new ProcessStartInfo("shutdown", arguments) { CreateNoWindow = true, UseShellExecute = false });
}
