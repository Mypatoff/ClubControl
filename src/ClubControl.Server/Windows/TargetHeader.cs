using System.Collections.Generic;
using System.Linq;
using ClubControl.Server.Resources;
using ClubControl.Shared;

namespace ClubControl.Server.Windows;

/// <summary>Shared "which PC(s)/billiard(s) is this action for" header, used by every bulk-capable dialog.</summary>
public static class TargetHeader
{
    public static string Build(List<PcRow> targets)
    {
        var first = targets[0];

        if (targets.Count == 1)
            return Strings.Format("TargetPcFormat", first.Number!, first.TariffDisplay);

        var kindWord = first.Kind == ResourceKind.Billiard ? Strings.Get("KindBilliardPlural") : Strings.Get("KindComputerPlural");
        var numbers = string.Join(", ", targets.OrderBy(t => t.Number).Select(t => t.Number));
        return Strings.Format("MultiTargetFormat", kindWord, numbers, first.TariffDisplay);
    }
}
