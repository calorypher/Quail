namespace Quail.App;

internal enum SettingsWindowLifecycleEvent
{
    Deactivated,
    Closed,
    Saved
}

internal static class SettingsWindowHotkeyLifecycle
{
    public static bool ShouldRestoreHotkey(bool captureActive, SettingsWindowLifecycleEvent @event) =>
        captureActive && @event is SettingsWindowLifecycleEvent.Deactivated or SettingsWindowLifecycleEvent.Closed;
}
