using System;
using System.Diagnostics;
using ClubControl.Shared;

namespace ClubControl.Client.Native;

/// <summary>Closes the processes named in the after-session process list, e.g. "steam.exe, csgo.exe".</summary>
public static class ProcessCloser
{
    public static void CloseAll(string? processListCsv)
    {
        // Parsing/validation (CSV handling, de-duplication, the Windows-critical-process
        // blocklist) is pure logic shared with the test suite - see ProcessNameList.
        foreach (var exeName in ProcessNameList.Parse(processListCsv))
        {
            foreach (var process in Process.GetProcessesByName(exeName))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // Already exiting on its own, or access denied - nothing more to do.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
