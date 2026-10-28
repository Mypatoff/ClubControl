using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClubControl.Server.Data;
using ClubControl.Server.Data.Models;
using ClubControl.Server.Hubs;
using ClubControl.Server.Native;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Server.Windows;
using ClubControl.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClubControl.Server;

public partial class App : Application
{
    private WebApplication? _host;
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private Mutex? _singleInstanceMutex;

    public IServiceProvider? Services => _host?.Services;

    public App()
    {
        // Safe default before Settings has even been read; OnStartup corrects this to the saved
        // preference once the database is available.
        ThemeManager.Apply(UiTheme.Light);

        // The theme's implicit <Style TargetType="Window"> only matches a plain Window - WPF matches the
        // exact type, so MainWindow, LoginWindow etc. would stay white with black text even in the dark
        // theme. Point every window at that style (as a resource reference, so it follows theme switches).
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
                ((Window)sender).SetResourceReference(FrameworkElement.StyleProperty, typeof(Window))));

        // Same idea for the title bar Windows draws: dark in the dark theme (see TitleBar).
        TitleBar.InstallForAllWindows();

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

        // SPEC section 1: single instance only. A second copy could not bind the same ports and
        // would fail with a raw socket error, so say so plainly and exit. "Global\" makes the
        // check work across Windows user sessions too.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\ClubControl.Server", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "ClubControl server is already running - look for its icon in the system tray.\n" +
                "ClubControl сервер уже запущен - найдите его значок в системном трее.",
                "ClubControl", MessageBoxButton.OK, MessageBoxImage.Information);
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // This is a tray app (SPEC section 1): closing the main window only hides it, so closing it
        // must never shut down the whole application on its own.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Directory.CreateDirectory(DbPaths.DataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(ClientUpdateFile.FilePath)!);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://0.0.0.0:{NetworkConstants.SignalRPort}");

        builder.Services.AddDbContext<ClubDbContext>(options =>
            options.UseSqlite(DbPaths.ConnectionString));
        // Detailed SignalR errors (full exception text/stack trace sent to the client) are only
        // safe in development - a stack trace handed to every terminal on the LAN is an
        // information-disclosure risk in production. Unhandled hub exceptions are still logged
        // server-side regardless (ASP.NET Core's default logging providers), just not echoed to
        // the caller.
        builder.Services.AddSignalR(options => options.EnableDetailedErrors = builder.Environment.IsDevelopment());
        builder.Services.AddScoped<SessionService>();
        builder.Services.AddSingleton<ClientUpdateTracker>();
        builder.Services.AddScoped<PowerService>();
        builder.Services.AddScoped<ReportsService>();
        builder.Services.AddHostedService<OfflineMonitorService>();
        builder.Services.AddHostedService<UdpDiscoveryService>();
        builder.Services.AddHostedService<SessionMonitorService>();
        builder.Services.AddHostedService<BackupService>();

        _host = builder.Build();
        _host.MapHub<ClubHub>(NetworkConstants.HubRoute);

        // Plain HTTP download for "Update client" (PowerService.UpdateClientAsync) - see
        // ClientUpdateFile for where the admin stages the build this serves.
        _host.MapGet(NetworkConstants.ClientUpdateRoute, () =>
            File.Exists(ClientUpdateFile.FilePath)
                ? Results.File(ClientUpdateFile.FilePath, "application/zip")
                : Results.NotFound());

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
            // Creates the database if it doesn't exist, or upgrades an existing one by applying
            // any migrations it is missing - this is what keeps schema changes from requiring a
            // wipe (see Data/Migrations; add a new migration via the "Add EF Migration" workflow).
            try
            {
                await DatabaseUpgrade.MigrateAsync(db, DbPaths.BackupDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"The database could not be upgraded.\nБазу данных не удалось обновить.\n\n{ex.Message}",
                    "ClubControl", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            var setting = await SettingsHelper.GetOrCreateAsync(db);
            await TariffsHelper.EnsureDefaultsAsync(db);

            Strings.Apply(setting.AdminPanelLanguage);
            ThemeManager.Apply(setting.UiTheme);

            // The server just (re)started, so no client is actually connected yet regardless of
            // what the database says from before the restart.
            await db.Pcs.ExecuteUpdateAsync(set => set.SetProperty(p => p.IsOnline, false));

            var sessionService = scope.ServiceProvider.GetRequiredService<SessionService>();
            await sessionService.RestoreActiveSessionsAsync();
        }

        // Database upgraded and sessions restored: this build is up. "Update server" waits for this file
        // before it keeps the new build (and puts the old one back if it never shows up).
        try
        {
            File.WriteAllText(ServerUpdateRelease.HealthMarkerPath, DateTime.UtcNow.ToString("O"));
        }
        catch (Exception)
        {
            // Only matters during an update, and then the old build is simply restored.
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
            // The exe's own icon (assets/ClubControl.ico); the generic one only if it cannot be read.
            Icon = (Environment.ProcessPath is { } exePath ? System.Drawing.Icon.ExtractAssociatedIcon(exePath) : null)
                   ?? System.Drawing.SystemIcons.Application,
            Text = "ClubControl",
            Visible = true,
            ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    /// <summary>
    /// Live theme switch (Settings > General): swaps the resource dictionary and recreates
    /// MainWindow, since its card borders/backgrounds are set directly in markup (not via an
    /// implicit style) and would not otherwise pick up the new colours without a restart.
    /// </summary>
    public void ApplyThemeAndRefreshWindows(UiTheme theme)
    {
        ThemeManager.Apply(theme);

        var old = MainWindow as MainWindow;
        var wasVisible = old?.IsVisible ?? true;

        MainWindow = new MainWindow();
        if (wasVisible)
            MainWindow.Show();

        old?.CloseForReal();
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
        if (await VerifyAdminPasswordAsync(null, LoginMode.VerifyForExit))
            Shutdown();
    }

    /// <summary>Asks for the admin password before something that closes or replaces the server (tray Exit, Update server).</summary>
    public async Task<bool> VerifyAdminPasswordAsync(Window? owner, LoginMode mode)
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClubDbContext>();
        var setting = await SettingsHelper.GetOrCreateAsync(db);

        var login = new LoginWindow(mode, setting.AdminPasswordHash);
        if (owner is not null)
            login.Owner = owner;
        return login.ShowDialog() == true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();

        if (_host is not null)
            await _host.StopAsync();

        // No ReleaseMutex: it must run on the owning thread, and Windows frees the mutex when the
        // process exits anyway.
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
