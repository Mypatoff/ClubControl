using System;
using System.Diagnostics;
using System.IO;

namespace ClubControl.Client.Native;

/// <summary>Closes the processes named in the after-session process list, e.g. "steam.exe, csgo.exe".</summary>
public static class ProcessCloser
{
    public static void CloseAll(string processListCsv)
    {
        var names = processListCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var name in names)
        {
            var exeName = Path.GetFileNameWithoutExtension(name);
            foreach (var process in Process.GetProcessesByName(exeName))
            {
                try
                {
                    process.Kill();
                }
                catch (Exception)
                {
                    // already exiting on its own - nothing to do
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }
}
