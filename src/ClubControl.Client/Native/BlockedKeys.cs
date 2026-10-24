namespace ClubControl.Client.Native;

/// <summary>
/// The combos the lock screen blocks (SPEC section 4): Win keys, Alt+Tab, Alt+Esc, Ctrl+Esc, Alt+F4.
/// Ctrl+Alt+Del cannot be blocked this way - Windows handles it before any hook sees it. That is accepted.
/// </summary>
public static class BlockedKeys
{
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_F4 = 0x73;

    public static bool IsBlockedCombo(int vkCode)
    {
        if (vkCode == VK_LWIN || vkCode == VK_RWIN)
            return true;

        if (vkCode == VK_TAB && NativeKeyboardHook.IsAltDown)
            return true;

        if (vkCode == VK_ESCAPE && (NativeKeyboardHook.IsAltDown || NativeKeyboardHook.IsControlDown))
            return true;

        if (vkCode == VK_F4 && NativeKeyboardHook.IsAltDown)
            return true;

        return false;
    }
}
