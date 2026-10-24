using System;
using System.IO;

namespace ClubControl.Server.Data;

public static class DbPaths
{
    public static string DataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClubControl");

    public static string DatabasePath => Path.Combine(DataDirectory, "club.db");

    public static string ConnectionString => $"Data Source={DatabasePath}";

    public static string BackupDirectory => Path.Combine(DataDirectory, "Backups");
}
