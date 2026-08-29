namespace Quail.App;

internal static class SettingsHotkeyRestoreGuard
{
    public const string ErrorMessage = "Could not restore the previous Quail hotkey. Close is blocked so Quail does not remain without an active hotkey.";

    public static bool TryRestore(Func<bool> restoreHotkey, out string? error)
    {
        if (restoreHotkey())
        {
            error = null;
            return true;
        }

        error = ErrorMessage;
        return false;
    }
}
