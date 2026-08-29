namespace Quail.App;

internal static class IndexManagerWindowLayout
{
    public const int InitialLogicalWidth = 800;
    public const int InitialLogicalHeight = 500;

    public static PhysicalSize InitialSizeToPhysical(uint dpi) => new(
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalWidth, dpi),
        QuickSearchOverlayLayout.ScaleToPhysical(InitialLogicalHeight, dpi));
}
