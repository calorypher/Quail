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
        Assert.True(QuickSearchLifecycle.ShouldHideOnDeactivation(overlayVisible: true, settingsDialogActive: false, exiting: false));
    }

    [Fact]
    public void Deactivation_does_not_hide_host_while_settings_are_active()
    {
        Assert.False(QuickSearchLifecycle.ShouldHideOnDeactivation(overlayVisible: true, settingsDialogActive: true, exiting: false));
    }

    [Fact]
    public void Repeated_summon_activates_visible_settings_without_compact_reset()
    {
        Assert.Equal(
            QuickSearchSummonBehavior.ActivateExistingSettings,
            QuickSearchLifecycle.GetSummonBehavior(overlayVisible: true, settingsDialogActive: true));
        Assert.False(QuickSearchLifecycle.ShouldToggleOverlayFromHotkey(settingsDialogActive: true));
    }

    [Fact]
    public void Opening_settings_from_hidden_overlay_still_uses_normal_summon()
    {
        Assert.Equal(
            QuickSearchSummonBehavior.ShowOverlay,
            QuickSearchLifecycle.GetSummonBehavior(overlayVisible: false, settingsDialogActive: true));
    }

    [Fact]
    public void Settings_deactivation_restores_an_active_hotkey_capture_only()
    {
        Assert.True(QuickSearchLifecycle.ShouldRestoreHotkeyOnSettingsDeactivation(settingsDialogActive: true, hotkeyCaptureActive: true));
        Assert.False(QuickSearchLifecycle.ShouldRestoreHotkeyOnSettingsDeactivation(settingsDialogActive: true, hotkeyCaptureActive: false));
        Assert.False(QuickSearchLifecycle.ShouldRestoreHotkeyOnSettingsDeactivation(settingsDialogActive: false, hotkeyCaptureActive: true));
    }
}
