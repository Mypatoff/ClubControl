namespace ClubControl.Shared;

/// <summary>Pure "which backups to delete" logic (SPEC section 10), shared by the server's backup service and its tests.</summary>
public static class BackupRetention
{
    public const int KeepCount = 30;

    /// <summary>
    /// Given every existing backup's timestamp (any order), returns the ones older than the
    /// newest <see cref="KeepCount"/> that should be deleted.
    /// </summary>
    public static List<DateTime> FilesToDelete(IEnumerable<DateTime> existingBackupTimestamps, int keepCount = KeepCount)
    {
        if (keepCount < 0)
            keepCount = 0;

        return existingBackupTimestamps
            .OrderByDescending(t => t)
            .Skip(keepCount)
            .ToList();
    }
}
