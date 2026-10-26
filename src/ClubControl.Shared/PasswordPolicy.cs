namespace ClubControl.Shared;

/// <summary>
/// Minimum requirement for the single admin password (SPEC section 7): just not empty. Pure
/// validation logic shared by the server's login/create/reset-password UI and its tests.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>True if the password meets the minimum bar; otherwise <paramref name="error"/> explains why.</summary>
    public static bool IsValid(string? password, out string? error)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "PasswordEmpty";
            return false;
        }

        error = null;
        return true;
    }
}
