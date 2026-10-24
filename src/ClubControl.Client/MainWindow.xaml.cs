using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Client.Services;

namespace ClubControl.Client;

/// <summary>
/// Hosts the connection bootstrap - never shown (see MainWindow.xaml/App.xaml.cs). Runs its
/// startup logic from the constructor rather than Loaded, since Loaded never fires for a window
/// that is never shown.
/// </summary>
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

        _ = ConnectToServerAsync();
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

    // Defense in depth: this window is never shown, so nothing should ever be able to close it -
    // but if something ever does, hide rather than let it tear down the whole process.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
