using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.Shared.Security;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace ClubControl.Server.Services;

/// <summary>
/// All session and billing logic (SPEC section 5). Pure arithmetic lives in
/// <see cref="BillingCalculator"/> (Shared, unit-tested); this class is the thin orchestration
/// layer that reads/writes the database, keeps <see cref="Pc.State"/> correct, logs events, and
/// pushes the resulting <see cref="ClientCommand"/> to whichever client is affected.
/// </summary>
public class SessionService
{
    private readonly ClubDbContext _db;
    private readonly IHubContext<ClubHub> _hub;

    public SessionService(ClubDbContext db, IHubContext<ClubHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // ---- prepay ----

    public async Task StartPrepayByMoneyAsync(Guid machineGuid, int moneyUzs)
    {
        var pc = await GetReadyPcAsync(machineGuid);
        var tariff = await GetTariffAsync(pc.TariffId!.Value);

        // Range-valid is not enough on its own - see IsValidPrepayStart: a payment at or just above
        // MinPaymentUzs can still round down to 0 minutes if the tariff's minimum is below the cost
        // of a single minute. Reject before any session/event is persisted.
        if (!BillingCalculator.IsValidPrepayStart(moneyUzs, tariff.PricePerHourUzs, tariff.MinPaymentUzs, tariff.MaxPaymentUzs))
            throw new InvalidOperationException(
                $"Payment must be between {tariff.MinPaymentUzs} and {tariff.MaxPaymentUzs} UZS, and buy at least one minute.");

        var minutes = BillingCalculator.MinutesForMoney(moneyUzs, tariff.PricePerHourUzs);
        await StartPrepayAsync(pc, tariff, minutes, moneyUzs);
    }

    public async Task StartPrepayByTimeAsync(Guid machineGuid, int minutes)
    {
        var pc = await GetReadyPcAsync(machineGuid);
        var tariff = await GetTariffAsync(pc.TariffId!.Value);

        var price = BillingCalculator.PriceForMinutes(minutes, tariff.PricePerHourUzs);
        if (!BillingCalculator.IsValidPrepayPayment(price, tariff.MinPaymentUzs, tariff.MaxPaymentUzs))
            throw new InvalidOperationException($"That much time costs {price} UZS, outside {tariff.MinPaymentUzs}-{tariff.MaxPaymentUzs} UZS.");

        await StartPrepayAsync(pc, tariff, minutes, price);
    }

    private async Task StartPrepayAsync(Pc pc, Tariff tariff, int minutes, int paidUzs)
    {
        var now = DateTime.UtcNow;
        var session = new Session
        {
            PcId = pc.Id,
            Type = SessionType.Prepay,
            StartUtc = now,
            PlannedEndUtc = now.AddMinutes(minutes),
            AmountUzs = paidUzs,
        };
        session.Segments.Add(new SessionSegment { PcId = pc.Id, PricePerHourUzs = tariff.PricePerHourUzs, StartUtc = now });
        _db.Sessions.Add(session);
        pc.State = PcState.PrepaySession;

        // The session row and its audit event must land together: if logging the event failed
        // after the session/PC-state save already committed, a real billing change would exist
        // with no corresponding event-log record. One transaction, two SaveChanges calls (the
        // first is needed so the just-added session gets a real Id to log before the second).
        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionStarted, pc.Id, session.Id, $"Prepay session started: {minutes} min, {paidUzs} UZS");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await SendCommandAsync(pc, new ClientCommand
        {
            Type = CommandType.StartSession,
            Session = new SessionInfo { Type = SessionType.Prepay, RemainingSeconds = minutes * 60 }
        });
    }

    public async Task ExtendPrepayByMoneyAsync(Guid machineGuid, int addMoneyUzs)
    {
        var (pc, session, segment, tariff) = await GetActivePrepayAsync(machineGuid);
        var addMinutes = BillingCalculator.MinutesForMoney(addMoneyUzs, segment.PricePerHourUzs);

        if (!BillingCalculator.IsValidExtendIncrement(addMoneyUzs, addMinutes))
            throw new InvalidOperationException($"{addMoneyUzs} UZS is not enough to buy any extra time at {segment.PricePerHourUzs} UZS/hour.");

        await ExtendPrepayAsync(pc, session, tariff, addMinutes, addMoneyUzs);
    }

    public async Task ExtendPrepayByTimeAsync(Guid machineGuid, int addMinutes)
    {
        var (pc, session, segment, tariff) = await GetActivePrepayAsync(machineGuid);
        var addMoneyUzs = BillingCalculator.PriceForMinutes(addMinutes, segment.PricePerHourUzs);

        if (!BillingCalculator.IsValidExtendIncrement(addMoneyUzs, addMinutes))
            throw new InvalidOperationException("Extend time must be greater than zero.");

        await ExtendPrepayAsync(pc, session, tariff, addMinutes, addMoneyUzs);
    }

    private async Task ExtendPrepayAsync(Pc pc, Session session, Tariff tariff, int addMinutes, int addMoneyUzs)
    {
        var newTotalPaid = session.AmountUzs + addMoneyUzs;
        if (!BillingCalculator.IsValidPrepayPayment(newTotalPaid, tariff.MinPaymentUzs, tariff.MaxPaymentUzs))
            throw new InvalidOperationException($"Extending would bring the total to {newTotalPaid} UZS, above the {tariff.MaxPaymentUzs} UZS maximum.");

        session.AmountUzs = newTotalPaid;
        session.PlannedEndUtc = session.PlannedEndUtc!.Value.AddMinutes(addMinutes);

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionExtended, pc.Id, session.Id, $"Extended by {addMinutes} min, {addMoneyUzs} UZS");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var remaining = Math.Max(0, (int)(session.PlannedEndUtc.Value - DateTime.UtcNow).TotalSeconds);
        await SendCommandAsync(pc, new ClientCommand
        {
            Type = CommandType.UpdateSession,
            Session = new SessionInfo { Type = SessionType.Prepay, RemainingSeconds = remaining }
        });
    }

    // ---- postpay / free ----

    public async Task StartPostpayAsync(Guid machineGuid)
    {
        var pc = await GetReadyPcAsync(machineGuid);
        var tariff = await GetTariffAsync(pc.TariffId!.Value);
        var now = DateTime.UtcNow;

        var session = new Session { PcId = pc.Id, Type = SessionType.Postpay, StartUtc = now };
        session.Segments.Add(new SessionSegment { PcId = pc.Id, PricePerHourUzs = tariff.PricePerHourUzs, StartUtc = now });
        _db.Sessions.Add(session);
        pc.State = PcState.PostpaySession;

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionStarted, pc.Id, session.Id, "Postpay session started");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await SendCommandAsync(pc, new ClientCommand
        {
            Type = CommandType.StartSession,
            Session = new SessionInfo
            {
                Type = SessionType.Postpay,
                ElapsedSeconds = 0,
                CurrentCostUzs = tariff.MinPaymentUzs,
                MaxPaymentUzs = tariff.MaxPaymentUzs
            }
        });
    }

    public async Task StartFreeAsync(Guid machineGuid, string? note)
    {
        var pc = await GetReadyPcAsync(machineGuid);
        var now = DateTime.UtcNow;

        // Free sessions still belong to a tariff for segment bookkeeping, but are never charged.
        var pricePerHourUzs = (await GetTariffAsync(pc.TariffId!.Value)).PricePerHourUzs;

        var session = new Session { PcId = pc.Id, Type = SessionType.Free, IsFree = true, StartUtc = now, AmountUzs = 0, Note = note };
        session.Segments.Add(new SessionSegment { PcId = pc.Id, PricePerHourUzs = pricePerHourUzs, StartUtc = now });
        _db.Sessions.Add(session);

        // Free sessions count up on screen exactly like postpay; there is no dedicated PcState for them.
        pc.State = PcState.PostpaySession;

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionStarted, pc.Id, session.Id, note is null ? "Free session started" : $"Free session started: {note}");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await SendCommandAsync(pc, new ClientCommand
        {
            Type = CommandType.StartSession,
            Session = new SessionInfo { Type = SessionType.Free, ElapsedSeconds = 0, CurrentCostUzs = 0, MaxPaymentUzs = 0 }
        });
    }

    /// <summary>
    /// The one "Stop"/"Paid" action for any active session: prepay ends with no refund, postpay's
    /// final cost is computed now, free stays at 0.
    /// </summary>
    public async Task EndSessionEarlyAsync(Guid machineGuid)
    {
        var pc = await GetPcAsync(machineGuid);
        if (pc.State is not (PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached))
            throw new InvalidOperationException("PC has no active session to end.");

        var session = await GetActiveSessionAsync(pc.Id)
            ?? throw new InvalidOperationException("No active session found.");

        var now = DateTime.UtcNow;

        if (session.Type == SessionType.Postpay)
        {
            var tariff = await GetTariffAsync(pc.TariffId!.Value);
            var elapsedPerSegment = session.Segments
                .Select(seg => (ElapsedSeconds: (int)((seg.EndUtc ?? now) - seg.StartUtc).TotalSeconds, seg.PricePerHourUzs))
                .ToList();
            session.AmountUzs = Math.Min(
                Math.Max(BillingCalculator.SumPostpaySegments(elapsedPerSegment), tariff.MinPaymentUzs),
                tariff.MaxPaymentUzs);
        }
        // Prepay: AmountUzs already holds what was paid - no refund for stopping early (SPEC 5).
        // Free: AmountUzs stays 0.

        var reason = pc.State == PcState.LimitReached ? "Admin confirmed payment" : "Admin stopped the session";
        await CloseSessionAsync(session, now, reason);
    }

    // ---- move ----

    public async Task MoveSessionAsync(Guid fromMachineGuid, Guid toMachineGuid)
    {
        var fromPc = await GetPcAsync(fromMachineGuid);
        if (fromPc.State is not (PcState.PrepaySession or PcState.PostpaySession))
            throw new InvalidOperationException("Source PC has no active session to move.");

        var toPc = await GetPcAsync(toMachineGuid);
        if (toPc.Id == fromPc.Id)
            throw new InvalidOperationException("Cannot move a session to the same PC.");
        if (!toPc.IsConfigured || !toPc.Enabled)
            throw new InvalidOperationException("Target PC is not available.");
        if (toPc.State is not (PcState.Free or PcState.Locked))
            throw new InvalidOperationException("Target PC is not free.");
        RequireSameTariff(fromPc, toPc);

        var session = await GetActiveSessionAsync(fromPc.Id)
            ?? throw new InvalidOperationException("No active session found on the source PC.");

        var now = DateTime.UtcNow;
        var sessionState = StateFor(session);
        ReassignSession(session, toPc, now);

        fromPc.State = PcState.Locked;
        toPc.State = sessionState;

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionMoved, toPc.Id, session.Id, $"Moved from PC #{fromPc.Number} to PC #{toPc.Number}");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await SendCommandAsync(fromPc, new ClientCommand { Type = CommandType.EndSession });
        await SendCommandAsync(fromPc, new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(fromPc, limitReached: false) });

        var sessionInfo = await BuildSessionInfoAsync(toPc, session);
        await SendCommandAsync(toPc, new ClientCommand { Type = CommandType.StartSession, Session = sessionInfo });
    }

    /// <summary>
    /// Both PCs are in a running session: the two sessions trade places. Same tariff only, so
    /// neither session's price changes and no time or money needs recalculating.
    /// </summary>
    public async Task SwapSessionsAsync(Guid firstMachineGuid, Guid secondMachineGuid)
    {
        var firstPc = await GetPcAsync(firstMachineGuid);
        var secondPc = await GetPcAsync(secondMachineGuid);
        if (firstPc.Id == secondPc.Id)
            throw new InvalidOperationException("Cannot swap a PC with itself.");

        foreach (var pc in new[] { firstPc, secondPc })
        {
            if (pc.State is not (PcState.PrepaySession or PcState.PostpaySession))
                throw new InvalidOperationException($"PC #{pc.Number} has no running session to swap.");
        }
        RequireSameTariff(firstPc, secondPc);

        var firstSession = await GetActiveSessionAsync(firstPc.Id)
            ?? throw new InvalidOperationException($"No active session found on PC #{firstPc.Number}.");
        var secondSession = await GetActiveSessionAsync(secondPc.Id)
            ?? throw new InvalidOperationException($"No active session found on PC #{secondPc.Number}.");

        var now = DateTime.UtcNow;
        var firstState = StateFor(firstSession);
        var secondState = StateFor(secondSession);

        ReassignSession(firstSession, secondPc, now);
        ReassignSession(secondSession, firstPc, now);
        firstPc.State = secondState;
        secondPc.State = firstState;

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionMoved, secondPc.Id, firstSession.Id, $"Swapped: moved from PC #{firstPc.Number} to PC #{secondPc.Number}");
            LogEvent(EventType.SessionMoved, firstPc.Id, secondSession.Id, $"Swapped: moved from PC #{secondPc.Number} to PC #{firstPc.Number}");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        // StartSession on a PC that is already in a session simply replaces what its timer shows.
        await SendCommandAsync(secondPc, new ClientCommand { Type = CommandType.StartSession, Session = await BuildSessionInfoAsync(secondPc, firstSession) });
        await SendCommandAsync(firstPc, new ClientCommand { Type = CommandType.StartSession, Session = await BuildSessionInfoAsync(firstPc, secondSession) });
    }

    /// <summary>A session only moves between resources of the same kind on the same tariff (a named resource never goes to a PC).</summary>
    private static void RequireSameTariff(Pc a, Pc b)
    {
        if (a.Kind != b.Kind || !a.TariffId.HasValue || a.TariffId != b.TariffId)
            throw new InvalidOperationException("A session can only be moved between resources of the same kind and tariff.");
    }

    private static PcState StateFor(Session session) =>
        session.Type == SessionType.Prepay ? PcState.PrepaySession : PcState.PostpaySession;

    /// <summary>Closes the session's current segment and opens a new one, at the same price, on <paramref name="toPc"/>.</summary>
    private static void ReassignSession(Session session, Pc toPc, DateTime now)
    {
        var oldSegment = session.Segments.First(s => s.EndUtc == null);
        oldSegment.EndUtc = now;
        session.Segments.Add(new SessionSegment { PcId = toPc.Id, PricePerHourUzs = oldSegment.PricePerHourUzs, StartUtc = now });
        session.PcId = toPc.Id;
    }

    // ---- background ticks (called by SessionMonitorService) and startup restore ----

    /// <summary>Power-cut restore (SPEC 5): closes prepay sessions whose planned end passed while the server was down.</summary>
    public Task RestoreActiveSessionsAsync() => EndExpiredPrepaySessionsAsync();

    public async Task EndExpiredPrepaySessionsAsync()
    {
        var now = DateTime.UtcNow;
        var expiring = await _db.Sessions
            .Include(s => s.Segments)
            .Where(s => s.Status == SessionStatus.Active && s.Type == SessionType.Prepay && s.PlannedEndUtc != null)
            .ToListAsync();

        foreach (var session in expiring)
        {
            if (BillingCalculator.IsPrepayExpired(session.PlannedEndUtc!.Value, now))
                await CloseSessionAsync(session, session.PlannedEndUtc.Value, "Prepay session time ran out");
        }
    }

    public async Task TickPostpayCostsAsync()
    {
        var now = DateTime.UtcNow;
        var sessions = await _db.Sessions
            .Include(s => s.Segments)
            .Include(s => s.Pc)
            .Where(s => s.Status == SessionStatus.Active && s.Type == SessionType.Postpay)
            .ToListAsync();

        foreach (var session in sessions)
        {
            var pc = session.Pc;
            if (pc is null || pc.State == PcState.LimitReached || !pc.TariffId.HasValue)
                continue;

            var openSegment = session.Segments.FirstOrDefault(s => s.EndUtc == null);
            if (openSegment is null)
                continue;

            var tariff = await TariffsHelper.GetAsync(_db, pc.TariffId.Value);
            if (tariff is null || !BillingCalculator.IsValidTariff(tariff.PricePerHourUzs, tariff.MinPaymentUzs, tariff.MaxPaymentUzs))
                continue; // misconfigured tariff: freeze this one session's cost rather than mis-bill it or crash the tick for every other PC

            var elapsedPerSegment = session.Segments
                .Select(seg => (ElapsedSeconds: (int)((seg.EndUtc ?? now) - seg.StartUtc).TotalSeconds, seg.PricePerHourUzs))
                .ToList();
            var totalElapsed = elapsedPerSegment.Sum(s => s.ElapsedSeconds);
            var cost = Math.Max(BillingCalculator.SumPostpaySegments(elapsedPerSegment), tariff.MinPaymentUzs);

            if (BillingCalculator.HasReachedLimit(cost, tariff.MaxPaymentUzs))
            {
                openSegment.EndUtc = now; // frozen - no more time counts once locked, waiting for the admin
                pc.State = PcState.LimitReached;

                await using (var transaction = await _db.Database.BeginTransactionAsync())
                {
                    await _db.SaveChangesAsync();
                    LogEvent(EventType.LimitReached, pc.Id, session.Id, $"PC #{pc.Number} reached the tariff maximum");
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                }

                await SendCommandAsync(pc, new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(pc, limitReached: true) });
            }
            else
            {
                await SendCommandAsync(pc, new ClientCommand
                {
                    Type = CommandType.UpdateSession,
                    Session = BuildPostpayInfo(session, now, totalElapsed, cost, tariff)
                });
            }
        }
    }

    /// <summary>
    /// What a PC should be doing right now, used to resync a client the moment it reconnects
    /// (SPEC 4 "Offline rule" and SPEC 5 "Power cuts / restarts").
    /// </summary>
    public async Task<ClientCommand?> BuildCurrentCommandAsync(Pc pc)
    {
        if (!pc.IsConfigured)
            return null;

        switch (pc.State)
        {
            case PcState.Free:
                return new ClientCommand { Type = CommandType.Unlock };

            case PcState.Locked:
                return new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(pc, limitReached: false) };

            case PcState.LimitReached:
                return new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(pc, limitReached: true) };

            case PcState.PrepaySession:
            case PcState.PostpaySession:
                var session = await GetActiveSessionAsync(pc.Id);
                return session is null
                    ? new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(pc, limitReached: false) }
                    : new ClientCommand { Type = CommandType.UpdateSession, Session = await BuildSessionInfoAsync(pc, session) };

            default:
                return null;
        }
    }

    // ---- read-only queries for the admin panel (Phase 4) ----

    /// <summary>
    /// One row's worth of session info for the main table: amount is "paid" for prepay, "current
    /// cost" otherwise. ConsumedUzs/CapUzs let the UI show progress toward a ceiling (money spent
    /// so far vs. the prepaid total, or current postpay cost vs. the tariff max) - CapUzs is 0 for
    /// free sessions, which have no such ceiling to show. RemainingMoneyUzs is the unused part of a
    /// prepay payment (0 for other sessions) - shown when the admin stops a prepay session early.
    /// </summary>
    public record ActiveSessionSnapshot(int SessionId, SessionType Type, DateTime StartUtc, DateTime? PlannedEndUtc,
        int AmountOrCostUzs, int RemainingSeconds, bool IsFree, int ConsumedUzs, int CapUzs, int RemainingMoneyUzs = 0);

    public async Task<Dictionary<int, ActiveSessionSnapshot>> GetActiveSessionSnapshotsAsync()
    {
        var sessions = await _db.Sessions
            .Include(s => s.Segments)
            .Include(s => s.Pc)
            .Where(s => s.Status == SessionStatus.Active)
            .ToListAsync();

        var result = new Dictionary<int, ActiveSessionSnapshot>();
        foreach (var session in sessions)
        {
            if (session.Pc is null)
                continue;

            result[session.PcId] = await BuildSnapshotAsync(session.Pc, session);
        }

        return result;
    }

    public async Task<ActiveSessionSnapshot?> GetActiveSessionSnapshotAsync(Guid machineGuid)
    {
        var pc = await GetPcAsync(machineGuid);
        var session = await GetActiveSessionAsync(pc.Id);
        return session is null ? null : await BuildSnapshotAsync(pc, session);
    }

    private async Task<ActiveSessionSnapshot> BuildSnapshotAsync(Pc pc, Session session)
    {
        var info = await BuildSessionInfoAsync(pc, session);
        var amountOrCost = session.Type == SessionType.Prepay ? session.AmountUzs : info.CurrentCostUzs;

        int consumedUzs;
        int capUzs;
        var remainingMoneyUzs = 0;

        if (session.Type == SessionType.Prepay && session.PlannedEndUtc.HasValue)
        {
            // Valued at the price of the PC the session is on right now (after a move, that is the new PC's price).
            var openSegment = session.Segments.FirstOrDefault(s => s.EndUtc == null);
            if (openSegment is not null)
                remainingMoneyUzs = BillingCalculator.RemainingPrepayMoney(info.RemainingSeconds, openSegment.PricePerHourUzs, session.AmountUzs);

            // Pure progress indicator (not a billing calculation) - how much of the prepaid time/
            // money has elapsed so far, proportional to the planned total.
            var totalPlannedSeconds = (session.PlannedEndUtc.Value - session.StartUtc).TotalSeconds;
            var elapsedSeconds = totalPlannedSeconds - info.RemainingSeconds;
            var fraction = totalPlannedSeconds > 0 ? Math.Clamp(elapsedSeconds / totalPlannedSeconds, 0, 1) : 0;
            capUzs = session.AmountUzs;
            consumedUzs = (int)Math.Round(capUzs * fraction);
        }
        else if (session.Type == SessionType.Postpay)
        {
            var tariff = await GetTariffAsync(pc.TariffId!.Value);
            capUzs = tariff.MaxPaymentUzs;
            consumedUzs = info.CurrentCostUzs;
        }
        else
        {
            capUzs = 0;
            consumedUzs = 0;
        }

        return new ActiveSessionSnapshot(session.Id, session.Type, session.StartUtc, session.PlannedEndUtc,
            amountOrCost, info.RemainingSeconds, session.IsFree, consumedUzs, capUzs, remainingMoneyUzs);
    }

    /// <summary>The ApplySettings command carrying the current General settings; sent on connect and whenever settings are saved.</summary>
    public async Task<ClientCommand> BuildApplySettingsCommandAsync()
    {
        var setting = await SettingsHelper.GetOrCreateAsync(_db);
        return new ClientCommand
        {
            Type = CommandType.ApplySettings,
            Settings = new ClientSettingsDto
            {
                ClubName = setting.ClubName,
                LockScreenText = setting.LockScreenText,
                LockScreenLanguage = setting.LockScreenLanguage,
                WarningMinutes1 = setting.WarningMinutes1,
                WarningMinutes2 = setting.WarningMinutes2,
                AfterSessionAction = setting.AfterSessionAction,
                ProcessListCsv = setting.ProcessListCsv,
                UnlockPasswordHash = setting.ClientUnlockPasswordHash,
                AutoShutdownAfterMinutes = setting.AutoShutdownAfterMinutes
            }
        };
    }

    /// <summary>Pushes the current General settings to every online, configured PC (SPEC section 8).</summary>
    public async Task BroadcastSettingsAsync()
    {
        var command = await BuildApplySettingsCommandAsync();

        var onlinePcs = await _db.Pcs.Where(p => p.IsOnline && p.Number != null).ToListAsync();
        foreach (var pc in onlinePcs)
            await SendCommandAsync(pc, command);
    }

    /// <summary>
    /// Mints this PC's permanent auth token (ClubHub.Heartbeat) the moment the admin finishes
    /// configuring it in Settings > Computers & Other, binding the token to whichever
    /// connection is registered for this exact MachineGuid *right now* - never to "whoever
    /// heartbeats next", which would let anyone who already knows or guessed the GUID (e.g. from
    /// an earlier heartbeat sniffed on the LAN) race the real PC for its own token at any point
    /// after being configured, with no further verification. Tying it to the admin's own Save
    /// action means the only way to win that race is to already be actively impersonating this
    /// PC's connection at the exact moment the admin clicks Save - which collides with and
    /// disrupts the real PC's own connection, unlike silently sniffing a GUID and waiting.
    /// Does nothing if this PC is not currently connected (not yet provisioned, or IsManual and
    /// never connects at all) - call this again after Settings is saved once it is online, e.g.
    /// simply by opening Settings and saving again for any reason.
    /// </summary>
    public async Task ProvisionPcIfNeededAsync(Pc pc)
    {
        if (!pc.IsConfigured || pc.AuthTokenHash is not null)
            return;

        if (!ClubHub.TryGetConnectionId(pc.MachineGuid, out var connectionId))
            return;

        var token = PcTokenHasher.GenerateToken();
        pc.AuthTokenHash = PcTokenHasher.Hash(token);
        await _db.SaveChangesAsync();

        await _hub.Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand,
            new ClientCommand { Type = CommandType.ProvisionToken, AuthToken = token });
    }

    // ---- shared helpers ----

    private async Task<Pc> GetPcAsync(Guid machineGuid) =>
        await _db.Pcs.FirstOrDefaultAsync(p => p.MachineGuid == machineGuid)
            ?? throw new InvalidOperationException("Unknown PC.");

    private async Task<Pc> GetReadyPcAsync(Guid machineGuid)
    {
        var pc = await GetPcAsync(machineGuid);
        if (!pc.IsConfigured)
            throw new InvalidOperationException("PC is not configured yet (no number/class assigned).");
        if (!pc.Enabled)
            throw new InvalidOperationException("PC is disabled.");
        if (pc.State is PcState.PrepaySession or PcState.PostpaySession or PcState.LimitReached)
            throw new InvalidOperationException("PC already has an active session.");
        return pc;
    }

    private async Task<Tariff> GetTariffAsync(int tariffId)
    {
        var tariff = await TariffsHelper.GetAsync(_db, tariffId)
            ?? throw new InvalidOperationException("That tariff no longer exists.");

        if (!BillingCalculator.IsValidTariff(tariff.PricePerHourUzs, tariff.MinPaymentUzs, tariff.MaxPaymentUzs))
            throw new InvalidOperationException(
                $"Tariff \"{tariff.Name}\" has no valid price/min/max configured and cannot be used to bill a session yet.");

        return tariff;
    }

    private Task<Session?> GetActiveSessionAsync(int pcId) =>
        _db.Sessions.Include(s => s.Segments)
            .Where(s => s.PcId == pcId && s.Status == SessionStatus.Active)
            .OrderByDescending(s => s.Id)
            .FirstOrDefaultAsync();

    private async Task<(Pc Pc, Session Session, SessionSegment Segment, Tariff Tariff)> GetActivePrepayAsync(Guid machineGuid)
    {
        var pc = await GetPcAsync(machineGuid);
        if (pc.State != PcState.PrepaySession)
            throw new InvalidOperationException("PC does not have an active prepay session.");

        var session = await GetActiveSessionAsync(pc.Id);
        if (session is null || session.Type != SessionType.Prepay)
            throw new InvalidOperationException("No active prepay session found.");

        var segment = session.Segments.First(s => s.EndUtc == null);
        var tariff = await GetTariffAsync(pc.TariffId!.Value);
        return (pc, session, segment, tariff);
    }

    private async Task<LockInfo> BuildLockInfoAsync(Pc pc, bool limitReached)
    {
        Tariff? tariff = pc.TariffId.HasValue ? await TariffsHelper.GetAsync(_db, pc.TariffId.Value) : null;

        return new LockInfo
        {
            LimitReached = limitReached,
            PcNumber = pc.Number,
            TariffName = tariff?.Name,
            PricePerHourUzs = tariff?.PricePerHourUzs ?? 0
        };
    }

    private async Task<SessionInfo> BuildSessionInfoAsync(Pc pc, Session session)
    {
        if (session.Type == SessionType.Prepay)
        {
            var remaining = Math.Max(0, (int)(session.PlannedEndUtc!.Value - DateTime.UtcNow).TotalSeconds);
            return new SessionInfo { Type = SessionType.Prepay, RemainingSeconds = remaining };
        }

        var now = DateTime.UtcNow;
        var elapsedPerSegment = session.Segments
            .Select(seg => (ElapsedSeconds: (int)((seg.EndUtc ?? now) - seg.StartUtc).TotalSeconds, seg.PricePerHourUzs))
            .ToList();
        var totalElapsed = elapsedPerSegment.Sum(s => s.ElapsedSeconds);

        if (session.Type == SessionType.Free)
            return new SessionInfo { Type = SessionType.Free, ElapsedSeconds = totalElapsed, CurrentCostUzs = 0, MaxPaymentUzs = 0 };

        var tariff = await GetTariffAsync(pc.TariffId!.Value);
        var cost = Math.Min(
            Math.Max(BillingCalculator.SumPostpaySegments(elapsedPerSegment), tariff.MinPaymentUzs),
            tariff.MaxPaymentUzs);
        return BuildPostpayInfo(session, now, totalElapsed, cost, tariff);
    }

    /// <summary>The postpay SessionInfo, including the pieces the client needs to keep the cost growing on its own.</summary>
    private static SessionInfo BuildPostpayInfo(Session session, DateTime now, int totalElapsed, int cost, Tariff tariff)
    {
        var openSegment = session.Segments.FirstOrDefault(s => s.EndUtc == null);
        var closedSegmentsCost = BillingCalculator.SumPostpaySegments(session.Segments
            .Where(s => s.EndUtc != null)
            .Select(s => ((int)(s.EndUtc!.Value - s.StartUtc).TotalSeconds, s.PricePerHourUzs)));

        return new SessionInfo
        {
            Type = SessionType.Postpay,
            ElapsedSeconds = totalElapsed,
            CurrentCostUzs = cost,
            MaxPaymentUzs = tariff.MaxPaymentUzs,
            ClosedSegmentsCostUzs = closedSegmentsCost,
            OpenSegmentElapsedSeconds = openSegment is null ? 0 : (int)(now - openSegment.StartUtc).TotalSeconds,
            PricePerHourUzs = openSegment?.PricePerHourUzs ?? 0,
            MinPaymentUzs = tariff.MinPaymentUzs
        };
    }

    private async Task CloseSessionAsync(Session session, DateTime endUtc, string reason)
    {
        var pc = await _db.Pcs.FindAsync(session.PcId) ?? throw new InvalidOperationException("PC not found.");

        var openSegment = session.Segments.FirstOrDefault(s => s.EndUtc == null);
        if (openSegment is not null)
            openSegment.EndUtc = endUtc;

        session.ActualEndUtc = endUtc;
        session.Status = SessionStatus.Ended;
        pc.State = PcState.Locked;

        await using (var transaction = await _db.Database.BeginTransactionAsync())
        {
            await _db.SaveChangesAsync();
            LogEvent(EventType.SessionEnded, pc.Id, session.Id, $"{reason} ({session.AmountUzs} UZS)");
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await SendCommandAsync(pc, new ClientCommand { Type = CommandType.EndSession });
        await SendCommandAsync(pc, new ClientCommand { Type = CommandType.Lock, Lock = await BuildLockInfoAsync(pc, limitReached: false) });
    }

    private void LogEvent(EventType type, int? pcId, int? sessionId, string message) =>
        _db.EventLogEntries.Add(new EventLogEntry { Type = type, PcId = pcId, SessionId = sessionId, Message = message });

    private async Task SendCommandAsync(Pc pc, ClientCommand command)
    {
        // Never address a command to a PC the server itself believes is offline, even if a
        // (possibly stale) connection mapping still exists - see ClubHub's connection lifecycle.
        if (!pc.IsOnline)
            return;

        if (ClubHub.TryGetConnectionId(pc.MachineGuid, out var connectionId))
            await _hub.Clients.Client(connectionId).SendAsync(HubMethodNames.ReceiveCommand, command);
    }
}
