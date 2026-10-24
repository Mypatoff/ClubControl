using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ClubControl.Shared;

namespace ClubControl.Server.Native;

/// <summary>
/// First-run firewall rules (SPEC section 2): TCP 5050 and UDP 5051 inbound, Private profile only.
/// Only ever adds allow rules - never touches or disables anything else. Requires the process to
/// be elevated; if it is not, netsh reports an error and the rule is simply missing until an admin
/// runs ClubControl (or install-server.ps1) elevated once. Safe to call on every startup.
/// </summary>
public static class FirewallSetup
{
    private const string TcpRuleName = "ClubControl Server (TCP 5050)";
    private const string UdpRuleName = "ClubControl Discovery (UDP 5051)";

    public static async Task EnsureRulesAsync()
    {
        await EnsureRuleAsync(TcpRuleName, "TCP", NetworkConstants.SignalRPort);
        await EnsureRuleAsync(UdpRuleName, "UDP", NetworkConstants.DiscoveryPort);
    }

    private static async Task EnsureRuleAsync(string name, string protocol, int port)
    {
        if (await RuleExistsAsync(name))
            return;

        await RunNetshAsync(
            $"advfirewall firewall add rule name=\"{name}\" dir=in action=allow protocol={protocol} localport={port} profile=private");
    }

    private static async Task<bool> RuleExistsAsync(string name)
    {
        var output = await RunNetshAsync($"advfirewall firewall show rule name=\"{name}\"");
        return !output.Contains("No rules match", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> RunNetshAsync(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("netsh", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        })!;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return output;
    }
}
