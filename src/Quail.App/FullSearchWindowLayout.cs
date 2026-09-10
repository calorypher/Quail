namespace Quail.App;

internal static class FullSearchWindowLayout
{
    public const int ResultLimit = 1_000;
    public const int InitialLogicalWidth = 1180;
    public const int InitialLogicalHeight = 760;
    public const int MinimumLogicalWidth = 820;
    public const int MinimumLogicalHeight = 560;

    public static PhysicalSize InitialSizeToPhysical(uint dpi) => new(
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalWidth, dpi),
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalHeight, dpi));

    public static PhysicalSize MinimumSizeToPhysical(uint dpi) => new(
        QuickSearchOverlayLayout.ScaleToPhysical(MinimumLogicalWidth, dpi),
        QuickSearchOverlayLayout.ScaleToPhysical(MinimumLogicalHeight, dpi));
}
