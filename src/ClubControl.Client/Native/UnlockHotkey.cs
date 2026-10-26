namespace ClubControl.Client.Native;

/// <summary>
/// The key combo that opens the unlock-password prompt on a locked PC: Ctrl+Shift+F12. It is
/// checked by the same keyboard hook that blocks Win/Alt+Tab, so it works whether or not the
/// server is reachable - the PC may have locked itself for no good reason (see SPEC section 4).
/// </summary>
public static class UnlockHotkey
{
    private const int VK_F12 = 0x7B;

    public static bool IsPressed(int vkCode) =>
        vkCode == VK_F12 && NativeKeyboardHook.IsControlDown && NativeKeyboardHook.IsShiftDown;
}
