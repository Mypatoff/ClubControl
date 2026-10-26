using System;
using System.Windows;
using System.Windows.Threading;
using ClubControl.Client.Resources;
using ClubControl.Shared;
using ClubControl.Shared.Security;

namespace ClubControl.Client.Windows;

/// <summary>
/// The password prompt opened by the unlock hotkey while the PC is locked. It checks the password
/// against the locally stored hash, so it works with or without the server. Wrong guesses are
/// throttled with <see cref="LoginLockout"/> (the same rule as the admin login).
/// </summary>
public partial class UnlockWindow : Window
{
    // Static so closing and re-opening the prompt does not reset the guess counter.
    private static int _failedAttempts;
    private static DateTime? _lockoutUntilUtc;

    private readonly string? _passwordHash;
    private readonly DispatcherTimer _lockoutTimer;

    /// <summary>Raised once, when the correct password was entered. The owner closes this window.</summary>
    public event Action? Unlocked;

    public UnlockWindow(string? passwordHash)
    {
        InitializeComponent();
        _passwordHash = passwordHash;

        TitleText.Text = Strings.Get("UnlockTitle");
        PromptText.Text = Strings.Get("UnlockPrompt");
        CancelButton.Content = Strings.Get("Cancel");
        OkButton.Content = Strings.Get("Ok");

        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += (_, _) => RefreshState();
        Closed += (_, _) => _lockoutTimer.Stop();

        Loaded += (_, _) =>
        {
            PasswordBox.Focus();
            RefreshState();
        };
    }

    /// <summary>Disables the form with a message while no password is set or a lockout is running.</summary>
    private void RefreshState()
    {
        if (string.IsNullOrEmpty(_passwordHash))
        {
            ErrorText.Text = Strings.Get("UnlockNotConfigured");
            SetInputEnabled(false);
            return;
        }

        if (LoginLockout.IsLockedOut(_lockoutUntilUtc, DateTime.UtcNow))
        {
            var secondsLeft = Math.Max(1, (int)Math.Ceiling((_lockoutUntilUtc!.Value - DateTime.UtcNow).TotalSeconds));
            ErrorText.Text = Strings.Format("UnlockLockedOut", secondsLeft);
            SetInputEnabled(false);
            _lockoutTimer.Start();
            return;
        }

        if (!PasswordBox.IsEnabled)
        {
            ErrorText.Text = "";
            SetInputEnabled(true);
            PasswordBox.Focus();
        }

        _lockoutTimer.Stop();
    }

    private void SetInputEnabled(bool enabled)
    {
        PasswordBox.IsEnabled = enabled;
        OkButton.IsEnabled = enabled;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        // Re-check right before verifying: the window may have been open since before a lockout started.
        RefreshState();
        if (!OkButton.IsEnabled)
            return;

        if (PasswordHasher.Verify(PasswordBox.Password, _passwordHash!))
        {
            (_failedAttempts, _lockoutUntilUtc) = LoginLockout.RecordSuccess();
            Unlocked?.Invoke();
            return;
        }

        (_failedAttempts, _lockoutUntilUtc) = LoginLockout.RecordFailure(_failedAttempts, DateTime.UtcNow);
        PasswordBox.Clear();
        RefreshState();

        if (OkButton.IsEnabled)
            ErrorText.Text = Strings.Get("UnlockWrongPassword");
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
