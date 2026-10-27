using System;

namespace ClubControl.Server;

/// <summary>How the admin panel writes times: when a session started and how long it has run.</summary>
public static class TimeText
{
    /// <summary>"14:05" for today, "27.10 14:05" for another day (a session can run past midnight).</summary>
    public static string StartedAt(DateTime utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm") : local.ToString("dd.MM HH:mm");
    }

    /// <summary>h:mm:ss, counting hours past 24 too (a plain TimeSpan format would wrap at a day).</summary>
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
