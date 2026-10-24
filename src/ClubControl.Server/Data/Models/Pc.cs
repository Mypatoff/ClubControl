using System;
using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>
/// A billable thing: a networked gaming PC (auto-discovered once its client connects), an
/// offline PC with no LAN connection, or a non-computer resource like a billiard table - the
/// latter two are created manually by the admin (<see cref="IsManual"/>) and never get a lock
/// screen, online tracking, or power commands, since there is no client running for them.
/// Unassigned/new PCs (Number/TariffId null) show as "New PC" in the admin panel.
/// </summary>
public class Pc
{
    public int Id { get; set; }
    public Guid MachineGuid { get; set; }

    public ResourceKind Kind { get; set; } = ResourceKind.Computer;
    public bool IsManual { get; set; }

    public int? Number { get; set; }
    public int? TariffId { get; set; }
    public bool Enabled { get; set; } = true;

    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Hostname { get; set; } = "";

    public PcState State { get; set; } = PcState.Free;

    public bool IsOnline { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }

    public bool IsConfigured => Number.HasValue && TariffId.HasValue;
}
