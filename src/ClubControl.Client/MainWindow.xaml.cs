using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Client.Services;

namespace ClubControl.Client;

public partial class MainWindow : Window
{
    private readonly AppController _controller = new();
    private ServerConnection? _connection;

    public MainWindow()
    {
        InitializeComponent();

        // Lock the PC and start the offline-wait clock right away - a client that cannot find a
        // server yet behaves exactly like one that lost a server it was already talking to.
        _controller.Start();

        Loaded += async (_, _) => await ConnectToServerAsync();
    }

    private async Task ConnectToServerAsync()
    {
        var machineGuid = MachineIdentity.GetMachineGuid();

        // DiscoverAsync with no cancellation retries forever, so this always eventually finds a server.
        var server = await ServerDiscoveryClient.DiscoverAsync(CancellationToken.None);
        DetailText.Text = $"Found server {server!.ServerId} at {server.ServerIp}:{server.Port}";

        _connection = new ServerConnection(server.ServerIp, server.Port, machineGuid);
        _connection.StateChanged += state => Dispatcher.Invoke(() => StatusText.Text = $"Connection: {state}");
        _controller.AttachConnection(_connection);

        StatusText.Text = "Connecting...";
        await _connection.ConnectAsync();
    }

    protected override async void OnClosed(EventArgs e)
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        base.OnClosed(e);
    }
}
