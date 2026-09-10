using Quail.App;

namespace Quail.Core.Tests;

public sealed class M11QuickSearchOverlayLayoutTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("a", 1)]
    public void Selects_the_expected_mode_for_the_trimmed_query(string query, int expected)
    {
        Assert.Equal((QuickSearchOverlayMode)expected, QuickSearchOverlayLayout.ForQuery(query));
    }

    [Fact]
    public void Uses_stable_compact_and_expanded_heights()
    {
        Assert.Equal(700, QuickSearchOverlayLayout.Width);
        Assert.Equal(56, QuickSearchOverlayLayout.GetHeight(QuickSearchOverlayMode.Compact));
        Assert.Equal(370, QuickSearchOverlayLayout.GetHeight(QuickSearchOverlayMode.Expanded));
    }

    [Theory]
    [InlineData(0, 96u, 700, 56)]
    [InlineData(1, 96u, 700, 370)]
    [InlineData(0, 144u, 1050, 84)]
    [InlineData(1, 144u, 1050, 555)]
    public void Converts_effective_layout_to_physical_pixels(int mode, uint dpi, int expectedWidth, int expectedHeight)
    {
        var actual = QuickSearchOverlayLayout.LogicalSizeToPhysical((QuickSearchOverlayMode)mode, dpi);

        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
    }

    [Theory]
    [InlineData(96u, 700, 500)]
    [InlineData(144u, 1050, 750)]
    public void Settings_host_size_scales_with_current_window_dpi(uint dpi, int expectedWidth, int expectedHeight)
    {
        var actual = QuickSearchOverlayLayout.LogicalSettingsSizeToPhysical(dpi);

        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
    }

    [Fact]
    public void Centers_a_window_within_its_monitor_work_area()
    {
        var position = QuickSearchOverlayLayout.CenterInWorkArea(
            workLeft: 1920,
            workTop: 40,
            workWidth: 2560,
            workHeight: 1400,
            windowSize: new PhysicalSize(1180, 760));

        Assert.Equal(new PhysicalPoint(2610, 360), position);
    }

    [Theory]
    [InlineData(96u, 800, 500)]
    [InlineData(144u, 1200, 750)]
    public void Index_manager_initial_size_scales_with_current_window_dpi(uint dpi, int expectedWidth, int expectedHeight)
    {
        var actual = IndexManagerWindowLayout.InitialSizeToPhysical(dpi);

        Assert.Equal(expectedWidth, actual.Width);
        Assert.Equal(expectedHeight, actual.Height);
    }

    [Fact]
    public void Deactivation_hides_normal_visible_quick_search()
    {
        Assert.True(QuickSearchLifecycle.ShouldHideOnDeactivation(overlayVisible: true, exiting: false));
    }

    [Fact]
    public void Deactivation_does_not_hide_quick_search_while_exiting()
    {
        Assert.False(QuickSearchLifecycle.ShouldHideOnDeactivation(overlayVisible: true, exiting: true));
    }
}

public sealed class SettingsWindowLayoutTests
{
    [Theory]
    [InlineData(96u, 920, 680)]
    [InlineData(144u, 1380, 1020)]
    public void Initial_size_scales_with_current_window_dpi(uint dpi, int width, int height)
    {
        var actual = SettingsWindowLayout.InitialSizeToPhysical(dpi);

        Assert.Equal(width, actual.Width);
        Assert.Equal(height, actual.Height);
    }
}
