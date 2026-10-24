using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data;
using ClubControl.Server.Hubs;
using ClubControl.Server.Native;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Server.Windows;
using ClubControl.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubControl.Server;

public partial class App : Application
{
    private WebApplication? _host;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public IServiceProvider? Services => _host?.Services;

    public App()
    {
        // Without this, an unhandled exception on any thread just silently kills the process -
        // no dialog, nothing in the event log the admin can see. Show what happened instead.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => ShowFatalError(args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            ShowFatalError(args.Exception);
            args.Handled = true;
            Shutdown();
        };
    }

    private static void ShowFatalError(Exception? ex) =>
        MessageBox.Show(ex?.ToString() ?? "Unknown error.", "ClubControl - unexpected error",
            MessageBoxButton.OK, MessageBoxImage.Error);

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // This is a tray app (SPEC section 1): closing the main window only hides it, so closing it
        // must never shut down the whole application on its own.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Directory.CreateDirectory(DbPaths.DataDirectory);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{NetworkConstants.SignalRPort}");

        builder.Services.AddDbContext<ClubDbContext>(options =>
            options.UseSqlite(DbPaths.ConnectionString));
        builder.Services.AddSignalR(options => options.EnableDetailedErrors = true);
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddScoped<PowerService>();
        builder.Services.AddScoped<ReportsService>();
        builder.Services.AddHostedService<OfflineMonitorService>();
        builder.Services.AddHostedService<UdpDiscoveryService>();
        builder.Services.AddHostedService<SessionMonitorService>();
        builder.Services.AddHostedService<BackupService>();

        _host = builder.Build();
        _host.MapHub<ClubHub>(NetworkConstants.HubRoute);

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            await db.Database.EnsureCreatedAsync();
            var setting = await SettingsHelper.GetOrCreateAsync(db);
            await TariffsHelper.EnsureDefaultsAsync(db);

            Strings.Apply(setting.AdminPanelLanguage);

            // The server just (re)started, so no client is actually connected yet regardless of
            // what the database says from before the restart.
            await db.Pcs.ExecuteUpdateAsync(set => set.SetProperty(p => p.IsOnline, false));

            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            await sessionService.RestoreActiveSessionsAsync();
        }

        // Best-effort (SPEC section 1/2): needs no elevation for the Run key, but firewall rules
        // need an elevated process - if this run is not elevated, netsh simply fails and the rules
        // stay missing until an admin runs ClubControl (or install-server.ps1) elevated once.
        AutoStart.Ensure();
        try
        {
            await FirewallSetup.EnsureRulesAsync();
        }
        catch (Exception)
        {
            // Never block startup on firewall setup - see comment above.
        }

        if (!await LogInAsync())
        {
            Shutdown();
            return;
        }

        await _host.StartAsync();

        CreateTrayIcon();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    /// <summary>Admin password prompt on open, first-run password creation, and the reset-password file (SPEC section 7).</summary>
    private async Task<bool> LogInAsync()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var setting = await SettingsHelper.GetOrCreateAsync(db);

        var resetFilePath = Path.Combine(AppContext.BaseDirectory, "reset-password");
        var resetRequested = File.Exists(resetFilePath);

        var mode = resetRequested ? LoginMode.ResetPassword
            : setting.AdminPasswordHash is null ? LoginMode.CreatePassword
            : LoginMode.Login;

        var login = new LoginWindow(mode, setting.AdminPasswordHash);
        if (login.ShowDialog() != true)
            return false;

        if (login.NewPasswordHash is not null)
        {
            setting.AdminPasswordHash = login.NewPasswordHash;
            await db.SaveChangesAsync();
        }

        if (resetRequested)
            File.Delete(resetFilePath);

        return true;
    }

    private void CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open panel", null, (_, _) => ShowMainWindow());
        menu.Items.Add("Exit", null, (_, _) => RequestExit());

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "ClubControl",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    private void ShowMainWindow()
    {
        if (MainWindow is null)
            return;

        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    /// <summary>Tray "Exit" requires the admin password (SPEC section 1) - this is the only real way to quit.</summary>
    private async void RequestExit()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var setting = await SettingsHelper.GetOrCreateAsync(db);

        var login = new LoginWindow(LoginMode.VerifyForExit, setting.AdminPasswordHash);
        if (login.ShowDialog() == true)
            Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();

        if (_host is not null)
            await _host.StopAsync();

        base.OnExit(e);
    }
}
