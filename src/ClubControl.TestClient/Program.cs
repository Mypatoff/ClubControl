using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ClubControl.Shared;
using ClubControl.Shared.Dto;
using ClubControl.TestClient;
using Microsoft.AspNetCore.SignalR.Client;

Console.WriteLine("ClubControl test client - a stand-in for the real client, used to check the server.");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cts.Cancel();
};

var machineGuid = MachineIdentity.GetOrCreateMachineGuid();
Console.WriteLine($"Machine GUID: {machineGuid}");

Console.WriteLine($"Broadcasting discovery on UDP {NetworkConstants.DiscoveryPort}...");
var server = await DiscoverServerAsync(cts.Token);
if (server is null)
{
    Console.WriteLine("Cancelled before a server was found.");
    return;
}

Console.WriteLine($"Found server {server.ServerId} at {server.ServerIp}:{server.Port}");

var connection = new HubConnectionBuilder()
    .WithUrl($"http://{server.ServerIp}:{server.Port}{NetworkConstants.HubRoute}")
    .WithAutomaticReconnect()
    .Build();

connection.Reconnecting += _ => { Console.WriteLine("Reconnecting..."); return Task.CompletedTask; };
connection.Reconnected += _ => { Console.WriteLine("Reconnected."); return Task.CompletedTask; };
connection.Closed += error => { Console.WriteLine($"Connection closed: {error?.Message}"); return Task.CompletedTask; };

await connection.StartAsync(cts.Token);
Console.WriteLine("Connected. Sending a heartbeat every 5 seconds. Press Ctrl+C to stop.");

Console.WriteLine();
Console.WriteLine("Phase 2 manual test mode: type commands below to send them to a real client.");
Console.Write("Target client's MachineGuid (from the server's PC list, blank to skip): ");
var targetInput = Console.ReadLine();
if (Guid.TryParse(targetInput, out var targetMachineGuid))
{
    PrintCommandHelp();
    _ = Task.Run(() => RunCommandReplAsync(connection, targetMachineGuid, cts.Token));
}
else
{
    Console.WriteLine("No target set - skipping command mode, just sending heartbeats.");
}

while (!cts.Token.IsCancellationRequested)
{
    if (connection.State == HubConnectionState.Connected)
    {
        try
        {
            await connection.InvokeAsync(HubMethodNames.Heartbeat, new HeartbeatRequest
            {
                MachineGuid = machineGuid,
                IpAddress = MachineIdentity.GetLocalIpAddress(),
                MacAddress = MachineIdentity.GetMacAddress(),
                Hostname = MachineIdentity.GetHostname(),
            }, cts.Token);

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] heartbeat sent");
        }
        catch (Exception ex) when (!cts.Token.IsCancellationRequested)
        {
            Console.WriteLine($"Heartbeat failed: {ex.Message}");
        }
    }

    try
    {
        await Task.Delay(TimeSpan.FromSeconds(5), cts.Token);
    }
    catch (OperationCanceledException)
    {
        break;
    }
}

await connection.DisposeAsync();
Console.WriteLine("Stopped.");

static async Task<DiscoveryResponse?> DiscoverServerAsync(CancellationToken cancellationToken)
{
    using var udpClient = new UdpClient { EnableBroadcast = true };
    var requestBytes = Encoding.UTF8.GetBytes(NetworkConstants.DiscoveryRequestText);
    var broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, NetworkConstants.DiscoveryPort);

    while (!cancellationToken.IsCancellationRequested)
    {
        await udpClient.SendAsync(requestBytes, requestBytes.Length, broadcastEndpoint);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            var result = await udpClient.ReceiveAsync(timeoutCts.Token);
            var text = Encoding.UTF8.GetString(result.Buffer);
            var response = DiscoveryResponse.TryParse(text);
            if (response is not null)
                return response;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Console.WriteLine("No reply yet, retrying...");
        }
    }

    return null;
}

static void PrintCommandHelp()
{
    Console.WriteLine("""
        Commands (case-insensitive):
          lock                                    - plain lock screen
          lock limit                               - "limit reached" lock screen
          lock <pcNumber> <tariffName> <priceUzs>  - lock screen with PC info
          unlock                                   - unlock, no session
          start prepay <remainingSeconds>
          start postpay <elapsedSeconds> <costUzs> [maxPaymentUzs]
          start free <elapsedSeconds>
          update prepay|postpay|free ...            - same shape as start, resyncs without resetting warnings
          end                                       - end session (runs the after-session action, then locks)
          shutdown
          restart
          settings <clubName> <warnMinutes1> <warnMinutes2> <offlineWaitSeconds>
          help

        Phase 3 - real server-side session/billing RPCs (target PC = the one set at startup):
          startprepaymoney <moneyUzs>     - start a prepay session, buying minutes with this money
          startprepaytime <minutes>       - start a prepay session for exactly this many minutes
          startpostpay                    - start a postpay (counts up) session
          startfree [note...]             - start a free session, amount is always 0
          extendmoney <moneyUzs>          - extend the active prepay session by this much money
          extendtime <minutes>            - extend the active prepay session by this many minutes
          move <toMachineGuid>            - move the active session to another (free) PC
          endsession                      - admin "Stop"/"Paid": end the active session now
        """);
}

static async Task RunCommandReplAsync(HubConnection connection, Guid targetMachineGuid, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        var line = await Console.In.ReadLineAsync(cancellationToken);
        if (line is null)
            return;

        if (string.IsNullOrWhiteSpace(line))
            continue;

        if (await TryRunSessionRpcAsync(connection, targetMachineGuid, line.Trim(), cancellationToken))
            continue;

        var command = ParseCommand(line.Trim());
        if (command is null)
        {
            Console.WriteLine("Not understood. Type 'help' for the command list.");
            continue;
        }

        try
        {
            await connection.InvokeAsync(HubMethodNames.SendTestCommand, targetMachineGuid, command, cancellationToken);
            Console.WriteLine($"Sent {command.Type}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send: {ex.Message}");
        }
    }
}

/// <summary>
/// Phase 3's real session/billing RPCs on the hub - these are the server's own session logic
/// (SessionService), not a test-only passthrough like SendTestCommand above. Returns false if the
/// line isn't one of these commands, so the caller falls back to the Phase 2 ClientCommand parser.
/// </summary>
static async Task<bool> TryRunSessionRpcAsync(HubConnection connection, Guid targetMachineGuid, string line, CancellationToken cancellationToken)
{
    var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var keyword = tokens[0].ToLowerInvariant();

    try
    {
        switch (keyword)
        {
            case "startprepaymoney" when tokens.Length == 2 && int.TryParse(tokens[1], out var moneyUzs):
                await connection.InvokeAsync("StartPrepayByMoney", targetMachineGuid, moneyUzs, cancellationToken);
                break;

            case "startprepaytime" when tokens.Length == 2 && int.TryParse(tokens[1], out var minutes):
                await connection.InvokeAsync("StartPrepayByTime", targetMachineGuid, minutes, cancellationToken);
                break;

            case "startpostpay" when tokens.Length == 1:
                await connection.InvokeAsync("StartPostpay", targetMachineGuid, cancellationToken);
                break;

            case "startfree":
                var note = tokens.Length > 1 ? string.Join(' ', tokens.Skip(1)) : null;
                await connection.InvokeAsync("StartFree", targetMachineGuid, note, cancellationToken);
                break;

            case "extendmoney" when tokens.Length == 2 && int.TryParse(tokens[1], out var addMoneyUzs):
                await connection.InvokeAsync("ExtendPrepayByMoney", targetMachineGuid, addMoneyUzs, cancellationToken);
                break;

            case "extendtime" when tokens.Length == 2 && int.TryParse(tokens[1], out var addMinutes):
                await connection.InvokeAsync("ExtendPrepayByTime", targetMachineGuid, addMinutes, cancellationToken);
                break;

            case "move" when tokens.Length == 2 && Guid.TryParse(tokens[1], out var toMachineGuid):
                await connection.InvokeAsync("MoveSession", targetMachineGuid, toMachineGuid, cancellationToken);
                break;

            case "endsession" when tokens.Length == 1:
                await connection.InvokeAsync("EndSessionEarly", targetMachineGuid, cancellationToken);
                break;

            default:
                return false;
        }

        Console.WriteLine("Done.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Rejected: {ex.Message}");
    }

    return true;
}

static ClientCommand? ParseCommand(string line)
{
    var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    var keyword = tokens[0].ToLowerInvariant();

    switch (keyword)
    {
        case "help":
            PrintCommandHelp();
            return null;

        case "lock" when tokens.Length == 1:
            return new ClientCommand { Type = CommandType.Lock, Lock = new LockInfo() };

        case "lock" when tokens.Length == 2 && tokens[1].Equals("limit", StringComparison.OrdinalIgnoreCase):
            return new ClientCommand { Type = CommandType.Lock, Lock = new LockInfo { LimitReached = true } };

        case "lock" when tokens.Length == 4
            && int.TryParse(tokens[1], out var pcNumber)
            && int.TryParse(tokens[3], out var priceUzs):
            return new ClientCommand
            {
                Type = CommandType.Lock,
                Lock = new LockInfo { PcNumber = pcNumber, TariffName = tokens[2], PricePerHourUzs = priceUzs }
            };

        case "unlock" when tokens.Length == 1:
            return new ClientCommand { Type = CommandType.Unlock };

        case "start" or "update":
            var commandType = keyword == "start" ? CommandType.StartSession : CommandType.UpdateSession;
            var session = ParseSession(tokens);
            return session is null ? null : new ClientCommand { Type = commandType, Session = session };

        case "end" when tokens.Length == 1:
            return new ClientCommand { Type = CommandType.EndSession };

        case "shutdown" when tokens.Length == 1:
            return new ClientCommand { Type = CommandType.Shutdown };

        case "restart" when tokens.Length == 1:
            return new ClientCommand { Type = CommandType.Restart };

        case "settings" when tokens.Length == 5
            && int.TryParse(tokens[2], out var warn1)
            && int.TryParse(tokens[3], out var warn2)
            && int.TryParse(tokens[4], out var offlineWait):
            return new ClientCommand
            {
                Type = CommandType.ApplySettings,
                Settings = new ClientSettingsDto
                {
                    ClubName = tokens[1],
                    WarningMinutes1 = warn1,
                    WarningMinutes2 = warn2,
                    OfflineUnlockWaitSeconds = offlineWait
                }
            };

        default:
            return null;
    }
}

static SessionInfo? ParseSession(string[] tokens)
{
    if (tokens.Length < 3)
        return null;

    if (!Enum.TryParse<SessionType>(tokens[1], true, out var type))
        return null;

    if (type == SessionType.Prepay)
    {
        return int.TryParse(tokens[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining)
            ? new SessionInfo { Type = SessionType.Prepay, RemainingSeconds = remaining }
            : null;
    }

    // Postpay and Free both count up and can show a running cost.
    if (tokens.Length < 3 || !int.TryParse(tokens[2], out var elapsed))
        return null;

    var cost = tokens.Length > 3 && int.TryParse(tokens[3], out var parsedCost) ? parsedCost : 0;
    var maxPayment = tokens.Length > 4 && int.TryParse(tokens[4], out var parsedMax) ? parsedMax : 0;

    return new SessionInfo
    {
        Type = type,
        ElapsedSeconds = elapsed,
        CurrentCostUzs = cost,
        MaxPaymentUzs = maxPayment
    };
}
