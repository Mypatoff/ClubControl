using System;
using System.IO;

namespace ClubControl.Server.Services;

/// <summary>
/// Where the admin drops the latest published client build for "Update client" to hand out - the
/// whole publish output folder (dotnet publish -c Release -r win-x64 --self-contained false),
/// zipped, overwriting this same file each time. One fixed path, no versioning: whatever is here
/// right now is what gets sent to a PC when asked.
/// </summary>
public static class ClientUpdateFile
{
    public static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ClubControl", "ClientUpdate", "ClubControl.Client.zip");
}
