namespace ClubControl.Shared;

/// <summary>
/// Pure CSV-to-safe-executable-names logic for the after-session process-closing list (SPEC
/// section 4), shared by the client's ProcessCloser and its tests.
/// </summary>
public static class ProcessNameList
{
    /// <summary>
    /// Never return these, no matter what ends up in the admin-configured process list (typo,
    /// copy-paste mistake, or a future UI bug that lets bad input through). The club's previous
    /// software used to lock PCs by killing and restarting explorer.exe, which was unstable and
    /// sometimes crashed - ClubControl must never reintroduce that failure mode, and the same
    /// reasoning applies to every other Windows-critical process.
    /// </summary>
    public static readonly IReadOnlySet<string> ProtectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "lsaiso", "svchost", "dwm", "fontdrvhost", "sihost", "ctfmon",
        "taskhostw", "spoolsv", "conhost", "dllhost", "runtimebroker", "searchindexer",
        "searchui", "searchapp", "shellexperiencehost", "startmenuexperiencehost",
        "textinputhost", "audiodg", "msmpeng", "securityhealthservice", "clubcontrol.client",
    };

    /// <summary>
    /// Turns the admin-configured CSV into safe, de-duplicated executable names to target: trims
    /// whitespace, drops empty/invalid entries, strips any path and extension down to just the
    /// file name (so "C:\Games\csgo.exe" and "csgo.exe" are treated the same, and an exact-name
    /// process match can never be tricked into matching an unrelated process), and always excludes
    /// Windows-critical process names regardless of what was typed in - see
    /// <see cref="ProtectedProcessNames"/>.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? processListCsv)
    {
        if (string.IsNullOrWhiteSpace(processListCsv))
            return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawName in processListCsv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var exeName = StripToExecutableName(rawName);

            if (exeName.Length == 0)
                continue;
            if (ProtectedProcessNames.Contains(exeName))
                continue;
            if (!seen.Add(exeName))
                continue; // duplicate entry - already queued

            result.Add(exeName);
        }

        return result;
    }

    /// <summary>
    /// Strips a possible path and extension down to just the executable name, e.g.
    /// "C:\Games\Steam\steam.exe" -> "steam". Done by hand rather than via
    /// <see cref="System.IO.Path"/>: that type's separator handling depends on the OS it is
    /// running on, which would make a backslash-containing entry parse differently on Windows
    /// (the real target) than wherever this logic happens to be unit-tested. This can never throw,
    /// however malformed the input.
    /// </summary>
    private static string StripToExecutableName(string rawName)
    {
        var separatorIndex = rawName.LastIndexOfAny(['\\', '/']);
        var fileName = separatorIndex >= 0 ? rawName[(separatorIndex + 1)..] : rawName;

        var dotIndex = fileName.LastIndexOf('.');
        var nameOnly = dotIndex > 0 ? fileName[..dotIndex] : fileName;

        return nameOnly.Trim();
    }
}
