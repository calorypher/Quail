namespace Quail.App;

internal enum QuickSearchSummonBehavior
{
    ShowOverlay,
    ActivateExistingSettings
}

internal static class QuickSearchLifecycle
{
    public static bool ShouldHideOnDeactivation(bool overlayVisible, bool settingsDialogActive, bool exiting) =>
        overlayVisible && !settingsDialogActive && !exiting;

    public static QuickSearchSummonBehavior GetSummonBehavior(bool overlayVisible, bool settingsDialogActive) =>
        overlayVisible && settingsDialogActive
            ? QuickSearchSummonBehavior.ActivateExistingSettings
            : QuickSearchSummonBehavior.ShowOverlay;

    public static bool ShouldToggleOverlayFromHotkey(bool settingsDialogActive) => !settingsDialogActive;

    public static bool ShouldRestoreHotkeyOnSettingsDeactivation(bool settingsDialogActive, bool hotkeyCaptureActive) =>
        settingsDialogActive && hotkeyCaptureActive;
}
