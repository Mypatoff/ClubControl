namespace ClubControl.Shared;

/// <summary>
/// Pure netsh argument-string construction for the server's inbound allow rules (SPEC section 2),
/// shared by FirewallSetup and its tests. Deliberately contains no "does this rule already exist"
/// text-parsing: netsh's human-readable output is localized (e.g. "No rules match the specified
/// criteria." only appears on English Windows), so a Contains() check against that text silently
/// never adds the rule on a non-English Windows install. FirewallSetup instead always runs Delete
/// then Add, which is locale-independent and idempotent regardless of what was there before.
/// </summary>
public static class FirewallRuleCommands
{
    /// <summary>Deletes every existing rule with this exact name, however many there are (0, 1, or duplicates).</summary>
    public static string DeleteRuleArguments(string ruleName) =>
        $"advfirewall firewall delete rule name=\"{ruleName}\"";

    /// <summary>
    /// Adds exactly one narrow inbound allow rule: this protocol/port only, Private profile only.
    /// Never generates a rule that allows more than that - see CLAUDE.md's firewall rule.
    /// </summary>
    public static string AddAllowRuleArguments(string ruleName, string protocol, int port) =>
        $"advfirewall firewall add rule name=\"{ruleName}\" dir=in action=allow protocol={protocol} localport={port} profile=private";
}
