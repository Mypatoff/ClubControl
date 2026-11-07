using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ClubControl.Server.Services;

/// <summary>
/// Times one repeated piece of work (the panel refresh, a monitor tick, a heartbeat) in steps and writes ONE line
/// to <c>server-log.txt</c> when it was slow, so "the server is laggy" can be traced to the step that really is
/// slow. A fast run writes nothing, and the same work logs at most once a minute so the log cannot fill up.
/// Usage: <c>var timer = SlowLog.Start("Refresh"); ...; timer.Step("sessions"); ...; timer.Finish();</c>
/// </summary>
public sealed class SlowLog
{
    /// <summary>A run shorter than this is normal and is not written.</summary>
    public static readonly TimeSpan Threshold = TimeSpan.FromMilliseconds(150);

    /// <summary>The same work is reported at most this often.</summary>
    public static readonly TimeSpan MinGap = TimeSpan.FromMinutes(1);

    private static readonly ConcurrentDictionary<string, DateTime> LastLoggedUtc = new();

    private readonly string _name;
    private readonly Stopwatch _total = Stopwatch.StartNew();
    private long _lastStepMs;
    private readonly List<(string Label, long Ms)> _steps = new();

    private SlowLog(string name) => _name = name;

    public static SlowLog Start(string name) => new(name);

    /// <summary>Marks the end of a step: the time since the previous step (or the start) is kept under this label.</summary>
    public void Step(string label)
    {
        var now = _total.ElapsedMilliseconds;
        _steps.Add((label, now - _lastStepMs));
        _lastStepMs = now;
    }

    /// <summary>Writes the line if the whole run was slow (and not reported a moment ago).</summary>
    public void Finish()
    {
        var elapsed = _total.Elapsed;
        var now = DateTime.UtcNow;
        if (!ShouldLog(elapsed, LastLoggedUtc.GetValueOrDefault(_name), now))
            return;

        LastLoggedUtc[_name] = now;
        ServerLog.Info("Slow", Describe(_name, elapsed, _steps));
    }

    /// <summary>Slow enough to report, and not reported within <see cref="MinGap"/>. Pure, so it is unit-tested.</summary>
    public static bool ShouldLog(TimeSpan elapsed, DateTime lastLoggedUtc, DateTime nowUtc) =>
        elapsed >= Threshold && nowUtc - lastLoggedUtc >= MinGap;

    /// <summary>"Refresh took 412 ms: sessions 300 ms, pcs 80 ms, events 32 ms".</summary>
    public static string Describe(string name, TimeSpan elapsed, IReadOnlyList<(string Label, long Ms)> steps)
    {
        var text = $"{name} took {(long)elapsed.TotalMilliseconds} ms";
        return steps.Count == 0 ? text : text + ": " + string.Join(", ", steps.Select(s => $"{s.Label} {s.Ms} ms"));
    }
}
