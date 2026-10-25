using System.Globalization;
using System.Text.RegularExpressions;

namespace ClubControl.Shared;

/// <summary>Pure "which backups to delete" logic (SPEC section 10), shared by the server's backup service and its tests.</summary>
public static class BackupRetention
{
    public const int KeepCount = 30;

    private static readonly Regex FileNamePattern = new(@"^club_(\d{4}-\d{2}-\d{2})\.db$", RegexOptions.Compiled);

    /// <summary>One backup file: its name (the unique identifier to delete by) and the date it is for.</summary>
    public readonly record struct BackupEntry(string FileName, DateTime Date);

    /// <summary>
    /// Given every existing backup file (any order), returns the file names older than the newest
    /// <see cref="KeepCount"/> that should be deleted. Age comes from the date each backup's own
    /// file name was created for (see <see cref="TryParseBackupDate"/>), never from filesystem
    /// metadata like creation/modified time - those can change on copy, restore, or across
    /// filesystems and would make retention nondeterministic. Selection is by file name, not by
    /// raw date equality, so two backups that happen to carry the same date (should not normally
    /// happen - one per day - but is not assumed) are each still decided individually rather than
    /// both being kept or both deleted together.
    /// </summary>
    public static List<string> FilesToDelete(IEnumerable<BackupEntry> existingBackups, int keepCount = KeepCount)
    {
        if (keepCount < 0)
            keepCount = 0;

        return existingBackups
            .OrderByDescending(b => b.Date)
            .ThenByDescending(b => b.FileName, StringComparer.Ordinal)
            .Skip(keepCount)
            .Select(b => b.FileName)
            .ToList();
    }

    /// <summary>
    /// Parses the yyyy-MM-dd date embedded in a backup file name like "club_2026-10-24.db". Returns
    /// null for anything that doesn't match - callers must never delete a file they cannot confirm
    /// is actually one of ours.
    /// </summary>
    public static DateTime? TryParseBackupDate(string fileName)
    {
        var match = FileNamePattern.Match(fileName);
        if (!match.Success)
            return null;

        return DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
