using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Services;
using ClubControl.Shared;

namespace ClubControl.Server.Windows;

/// <summary>
/// Admin panel "Update server": shows the installed and the newest build on GitHub, and on a click
/// (after the admin password) downloads it, refreshes the client update file and restarts the server
/// on the new build. See <see cref="ServerUpdater"/>.
/// </summary>
public partial class ServerUpdateWindow : Window
{
    private readonly ServerUpdater _updater = new();
    private readonly CancellationTokenSource _cancel = new();
    private ReleaseInfo? _release;
    private bool _working;

    public ServerUpdateWindow()
    {
        InitializeComponent();

        Title = Strings.Get("UpdateServerTitle");
        HeaderText.Text = Strings.Get("UpdateServerTitle");
        InstalledText.Text = Strings.Format("UpdateServerInstalled", ServerUpdater.ShortVersion(ServerUpdater.InstalledVersion));
        NoteText.Text = Strings.Get("UpdateServerNote");
        OpenPageButton.Content = Strings.Get("UpdateServerOpenPage");
        CloseButton.Content = Strings.Get("Close");
        UpdateButton.Content = Strings.Get("UpdateServerButton");

        Loaded += async (_, _) => await CheckAsync();
        Closed += (_, _) => _cancel.Cancel();
    }

    private async System.Threading.Tasks.Task CheckAsync()
    {
        StatusText.Text = Strings.Get("UpdateServerChecking");

        try
        {
            _release = await _updater.CheckAsync(_cancel.Token);
        }
        catch (Exception ex) when (!_cancel.IsCancellationRequested)
        {
            StatusText.Text = "";
            ErrorText.Text = Strings.Format("UpdateServerCheckFailed", ex.Message);
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var shortSha = _release.CommitSha.Length >= 7 ? "+" + _release.CommitSha[..7] : "";
        LatestText.Text = Strings.Format("UpdateServerLatest", shortSha, _release.PublishedUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"));

        var isNewer = ServerUpdateRelease.IsNewer(ServerUpdater.InstalledVersion, _release.CommitSha);
        StatusText.Text = Strings.Get(isNewer ? "UpdateServerNewer" : "UpdateServerUpToDate");
        UpdateButton.Content = Strings.Get(isNewer ? "UpdateServerButton" : "UpdateServerButtonAnyway");

        if (!ServerUpdater.InstallFolderIsWritable())
        {
            ErrorText.Text = Strings.Format("UpdateServerProtectedFolder", ServerUpdater.InstallDir);
            return;
        }

        UpdateButton.IsEnabled = true;
    }

    private async void OnUpdate(object sender, RoutedEventArgs e)
    {
        if (_release is null || _working)
            return;

        // The server closes and restarts, so it is as sensitive as Exit in the tray: ask for the admin password.
        if (!await ((App)Application.Current).VerifyAdminPasswordAsync(this, LoginMode.VerifyForUpdate))
            return;

        _working = true;
        UpdateButton.IsEnabled = false;
        CloseButton.IsEnabled = false;
        ErrorText.Text = "";
        DownloadBar.Visibility = Visibility.Visible;

        try
        {
            var progress = new Progress<double>(fraction =>
            {
                DownloadBar.Value = fraction;
                StatusText.Text = Strings.Format("UpdateServerDownloading", (int)(fraction * 100));
            });

            var staged = await _updater.DownloadAsync(_release, progress, _cancel.Token);

            StatusText.Text = Strings.Get("UpdateServerInstalling");
            ServerUpdater.StartSwap(staged);

            // The script waits for this process to exit, then swaps the files and starts the new build.
            DialogResult = true;
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (!_cancel.IsCancellationRequested)
        {
            _working = false;
            DownloadBar.Visibility = Visibility.Collapsed;
            StatusText.Text = "";
            ErrorText.Text = Strings.Format("UpdateServerFailed", ex.Message);
            UpdateButton.IsEnabled = true;
            CloseButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            // The window was closed while downloading.
        }
    }

    private void OnOpenPage(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(ServerUpdateRelease.PageUrl) { UseShellExecute = true });

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
