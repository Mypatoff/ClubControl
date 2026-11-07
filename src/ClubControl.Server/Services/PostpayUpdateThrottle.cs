using System;
using System.Collections.Generic;
using System.Linq;

namespace ClubControl.Server.Services;

/// <summary>
/// Decides when a running postpay session should be sent to its client again. The client counts the cost
/// by itself between messages, so it does not need one every 2 seconds: one every <see cref="Every"/> only
/// corrects clock drift. (Reaching the tariff maximum is still checked by the server on every tick.)
/// </summary>
public class PostpayUpdateThrottle
{
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ForgetAfter = TimeSpan.FromMinutes(10);

    private readonly Dictionary<int, DateTime> _lastSentUtc = new();

    /// <summary>True the first time a session is asked about, and then once per <see cref="Every"/>.</summary>
    public bool ShouldSend(int sessionId, DateTime nowUtc)
    {
        if (_lastSentUtc.Count > 64)
        {
            // Sessions that ended leave their entry behind; sweep the old ones now and then.
            foreach (var old in _lastSentUtc.Where(e => nowUtc - e.Value > ForgetAfter).Select(e => e.Key).ToList())
                _lastSentUtc.Remove(old);
        }

        if (_lastSentUtc.TryGetValue(sessionId, out var last) && nowUtc - last < Every)
            return false;

        _lastSentUtc[sessionId] = nowUtc;
        return true;
    }
}
