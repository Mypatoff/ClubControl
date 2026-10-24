using System;
using ClubControl.Shared;

namespace ClubControl.Server.Data.Models;

/// <summary>A gaming PC. Unassigned PCs (Number/Class null) show as "New PC" in the admin panel.</summary>
public class Pc
{
    public int Id { get; set; }
    public Guid MachineGuid { get; set; }

    public int? Number { get; set; }
    public PcClass? Class { get; set; }
    public bool Enabled { get; set; } = true;

    public string IpAddress { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string Hostname { get; set; } = "";

    public PcState State { get; set; } = PcState.Free;

    public bool IsOnline { get; set; }
    public DateTime LastHeartbeatUtc { get; set; }

    public bool IsConfigured => Number.HasValue && Class.HasValue;
}
