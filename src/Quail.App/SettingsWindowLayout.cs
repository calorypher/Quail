namespace Quail.App;

internal static class SettingsWindowLayout
{
    public const int InitialLogicalWidth = 920;
    public const int InitialLogicalHeight = 680;

    public static PhysicalSize InitialSizeToPhysical(uint dpi) => new(
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalWidth, dpi),
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalHeight, dpi));
}
