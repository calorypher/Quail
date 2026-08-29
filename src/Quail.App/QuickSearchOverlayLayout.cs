namespace Quail.App;

internal enum QuickSearchOverlayMode
{
    Compact,
    Expanded
}

internal static class QuickSearchOverlayLayout
{
    private const int DefaultDpi = 96;
    public const int Width = 700;
    public const int CompactHeight = 56;
    public const int ExpandedHeight = 370;
    public const int SettingsHeight = 500;

    public static QuickSearchOverlayMode ForQuery(string query) =>
        string.IsNullOrWhiteSpace(query) ? QuickSearchOverlayMode.Compact : QuickSearchOverlayMode.Expanded;

    public static int GetHeight(QuickSearchOverlayMode mode) => mode switch
    {
        QuickSearchOverlayMode.Compact => CompactHeight,
        QuickSearchOverlayMode.Expanded => ExpandedHeight,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
    };

    public static PhysicalSize LogicalSizeToPhysical(QuickSearchOverlayMode mode, uint dpi) => new(
        ScaleToPhysical(Width, dpi),
        ScaleToPhysical(GetHeight(mode), dpi));

    public static PhysicalSize LogicalSettingsSizeToPhysical(uint dpi) => new(
        ScaleToPhysical(Width, dpi),
        ScaleToPhysical(SettingsHeight, dpi));

    internal static int ScaleToPhysical(int logicalPixels, uint dpi) =>
        checked((int)Math.Round(logicalPixels * (double)dpi / DefaultDpi, MidpointRounding.AwayFromZero));
}

internal readonly record struct PhysicalSize(int Width, int Height);
