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

    /// <summary>
    /// Makes exactly one matching rule exist, regardless of what was there before. Deliberately
    /// does not try to detect "does the rule already exist" by parsing netsh's human-readable
    /// output first - that text is localized (e.g. "No rules match the specified criteria." only
    /// appears in English Windows), so a locale check like `Contains("No rules match")` silently
    /// never adds the rule on a non-English Windows install and the firewall stays closed. Delete
    /// (matching by name only, so it removes any number of stale/duplicate rules with this exact
    /// name) then add is locale-independent and idempotent either way.
    /// </summary>
    private static async Task EnsureRuleAsync(string name, string protocol, int port)
    {
        await RunNetshAsync(FirewallRuleCommands.DeleteRuleArguments(name));

        var addResult = await RunNetshAsync(FirewallRuleCommands.AddAllowRuleArguments(name, protocol, port));

        if (addResult.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"netsh failed to add firewall rule \"{name}\" (exit code {addResult.ExitCode}). " +
                "The process is likely not running elevated.");
        }
    }

    private static async Task<(int ExitCode, string Output)> RunNetshAsync(string arguments)
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
        return (process.ExitCode, output);
    }
}
