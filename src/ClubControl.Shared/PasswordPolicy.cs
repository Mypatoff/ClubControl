namespace ClubControl.Shared;

/// <summary>
/// Practical minimum requirements for the single admin password (SPEC section 7). Pure validation
/// logic shared by the server's login/create/reset-password UI and its tests.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    /// <summary>True if the password meets the minimum bar; otherwise <paramref name="error"/> explains why.</summary>
    public static bool IsValid(string? password, out string? error)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "PasswordEmpty";
            return false;
        }

        if (password.Length < MinLength)
        {
            error = "PasswordTooShort";
            return false;
        }

        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
        {
            error = "PasswordNeedsLetterAndDigit";
            return false;
        }

        error = null;
        return true;
    }
}
