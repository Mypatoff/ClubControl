using System;
using System.Windows;
using System.Windows.Threading;
using ClubControl.Server.Resources;
using ClubControl.Server.Security;
using ClubControl.Shared;
using ClubControl.Shared.Security;

namespace ClubControl.Server.Windows;

public enum LoginMode
{
    Login,
    CreatePassword,
    ResetPassword,
    VerifyForExit
}

/// <summary>
/// One window for every admin-password flow in SPEC section 7: the normal login prompt, the
/// first-run "create a password" form, the reset-password form (triggered by the reset-password
/// file), and the password check before Exit from the tray.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly LoginMode _mode;
    private readonly string? _currentHash;
    private readonly DispatcherTimer _lockoutTimer;

    /// <summary>Set when Create/Reset succeeds - the caller is responsible for saving it.</summary>
    public string? NewPasswordHash { get; private set; }

    public LoginWindow(LoginMode mode, string? currentHash)
    {
        InitializeComponent();
        _mode = mode;
        _currentHash = currentHash;

        PasswordLabel.Text = Strings.Get("PasswordLabel");
        ConfirmLabel.Text = Strings.Get("ConfirmPasswordLabel");
        CancelButton.Content = Strings.Get("Cancel");
        OkButton.Content = Strings.Get("Ok");

        var needsConfirm = mode is LoginMode.CreatePassword or LoginMode.ResetPassword;
        ConfirmLabel.Visibility = needsConfirm ? Visibility.Visible : Visibility.Collapsed;
        ConfirmPasswordBox.Visibility = needsConfirm ? Visibility.Visible : Visibility.Collapsed;

        // The very first login (nothing to cancel back to) has no Cancel button.
        CancelButton.Visibility = mode == LoginMode.Login ? Visibility.Collapsed : Visibility.Visible;

        TitleText.Text = mode switch
        {
            LoginMode.CreatePassword => Strings.Get("TitleCreatePassword"),
            LoginMode.ResetPassword => Strings.Get("TitleResetPassword"),
            LoginMode.VerifyForExit => Strings.Get("TitleVerifyForExit"),
            _ => Strings.Get("TitleLogin")
        };

        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += (_, _) => RefreshLockoutState();

        Loaded += (_, _) =>
        {
            PasswordBox.Focus();
            RefreshLockoutState();
        };
    }

    /// <summary>
    /// Password verification (Login/VerifyForExit only - Create/Reset have nothing to brute-force
    /// yet) is throttled via <see cref="LoginAttemptGuard"/>. While locked out, the form is disabled
    /// and shows a live countdown instead of silently rejecting input.
    /// </summary>
    private void RefreshLockoutState()
    {
        if (_mode is not (LoginMode.Login or LoginMode.VerifyForExit))
            return;

        if (LoginAttemptGuard.IsLockedOut(out var remaining))
        {
            var secondsLeft = Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds));
            ErrorText.Text = Strings.Format("LoginLockedOut", secondsLeft);
            OkButton.IsEnabled = false;
            PasswordBox.IsEnabled = false;
            _lockoutTimer.Start();
        }
        else
        {
            OkButton.IsEnabled = true;
            PasswordBox.IsEnabled = true;
            _lockoutTimer.Stop();
        }
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (_mode is LoginMode.CreatePassword or LoginMode.ResetPassword)
        {
            if (!PasswordPolicy.IsValid(password, out var error))
            {
                ErrorText.Text = Strings.Get(error!);
                return;
            }

            if (password != ConfirmPasswordBox.Password)
            {
                ErrorText.Text = Strings.Get("PasswordsDontMatch");
                ConfirmPasswordBox.Clear();
                return;
            }

            NewPasswordHash = PasswordHasher.Hash(password);
            DialogResult = true;
            return;
        }

        // Login / VerifyForExit: re-check the lockout right before verifying, not just at window
        // load, since the form could have been sitting open since before a lockout expired or - if
        // someone scripted repeated Enter presses - since before one started.
        RefreshLockoutState();
        if (!OkButton.IsEnabled)
            return;

        if (_currentHash is not null && PasswordHasher.Verify(password, _currentHash))
        {
            LoginAttemptGuard.RecordSuccess();
            DialogResult = true;
            return;
        }

        LoginAttemptGuard.RecordFailure();
        RefreshLockoutState();

        if (OkButton.IsEnabled)
        {
            ErrorText.Text = Strings.Get("WrongPassword");
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _lockoutTimer.Stop();
        DialogResult = false;
    }
}
