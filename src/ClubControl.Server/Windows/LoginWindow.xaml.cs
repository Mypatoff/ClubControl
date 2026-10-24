using System.Windows;
using ClubControl.Server.Resources;
using ClubControl.Server.Security;

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

        Loaded += (_, _) => PasswordBox.Focus();
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        var password = PasswordBox.Password;

        if (_mode is LoginMode.CreatePassword or LoginMode.ResetPassword)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                ErrorText.Text = Strings.Get("PasswordEmpty");
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

        if (_currentHash is not null && PasswordHasher.Verify(password, _currentHash))
        {
            DialogResult = true;
            return;
        }

        ErrorText.Text = Strings.Get("WrongPassword");
        PasswordBox.Clear();
        PasswordBox.Focus();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
