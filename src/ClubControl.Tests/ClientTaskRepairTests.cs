using System.Text;
using ClubControl.Shared;
using Xunit;

namespace ClubControl.Tests;

public class ClientTaskRepairTests
{
    [Fact]
    public void Script_TargetsTheClientTaskByDefault()
    {
        Assert.Contains("$name = 'ClubControl Client'", ClientTaskRepair.BuildScript());
    }

    [Fact]
    public void Script_QuotesTheTaskNameSoAnApostropheCannotBreakOutOfTheString()
    {
        Assert.Contains("$name = 'Bob''s task'", ClientTaskRepair.BuildScript("Bob's task"));
    }

    [Fact]
    public void Script_DoesNothingWhenThereIsNoTaskOrItAlreadyRepeatsEveryMinute()
    {
        var script = ClientTaskRepair.BuildScript();

        Assert.Contains("if (-not $task) { exit 0 }", script);
        Assert.Contains("$logon[0].Repetition.Interval -eq 'PT1M') { exit 0 }", script);
    }

    [Fact]
    public void Script_AddsAOneMinuteRepetitionToTheLogonTriggerOnly()
    {
        var script = ClientTaskRepair.BuildScript();

        Assert.Contains("New-ScheduledTaskTrigger -AtLogOn -User $user", script);
        Assert.Contains("-RepetitionInterval (New-TimeSpan -Minutes 1)", script);
        Assert.Contains("$trigger.Repetition = $repeat.Repetition", script);
        // Only triggers are replaced - the action, user and settings of the task are not re-registered.
        Assert.Contains("Set-ScheduledTask -TaskName $name -Trigger", script);
        Assert.DoesNotContain("Register-ScheduledTask", script);
        Assert.DoesNotContain("Unregister-ScheduledTask", script);
    }

    [Fact]
    public void Script_ExitsWithTheUpdatedCodeOnlyAfterChangingTheTask()
    {
        var script = ClientTaskRepair.BuildScript();

        Assert.True(script.IndexOf("Set-ScheduledTask", StringComparison.Ordinal)
                    < script.IndexOf($"exit {ClientTaskRepair.ExitCodeUpdated}", StringComparison.Ordinal));
    }

    [Fact]
    public void EncodedCommand_IsTheScriptAsBase64Utf16()
    {
        var encoded = ClientTaskRepair.ToEncodedCommand("exit 0 # ёж");

        Assert.Equal("exit 0 # ёж", Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
    }
}
