using Quail.App;

namespace Quail.Core.Tests;

public sealed class M10HotkeyCaptureTests
{
    [Fact]
    public void TryCapture_CanonicalizesAllowedCombination()
    {
        var captured = HotkeyCapture.TryCapture('q', controlDown: true, altDown: true, shiftDown: false, winDown: false, out var displayText);

        Assert.True(captured);
        Assert.Equal("Ctrl+Alt+Q", displayText);
    }

    [Theory]
    [InlineData(0x11u)]
    [InlineData(0x12u)]
    [InlineData(0x10u)]
    [InlineData(0x5Bu)]
    public void TryCapture_RejectsModifierOnlyKeys(uint virtualKey)
    {
        var captured = HotkeyCapture.TryCapture(virtualKey, controlDown: true, altDown: true, shiftDown: false, winDown: false, out var displayText);

        Assert.False(captured);
        Assert.Equal(string.Empty, displayText);
    }

    [Fact]
    public void TryCapture_RequiresAtLeastOneModifier()
    {
        var captured = HotkeyCapture.TryCapture('Q', controlDown: false, altDown: false, shiftDown: false, winDown: false, out var displayText);

        Assert.False(captured);
        Assert.Equal(string.Empty, displayText);
    }

    [Fact]
    public void TryCapture_AcceptsSpaceAndCanonicalModifierOrder()
    {
        var captured = HotkeyCapture.TryCapture(HotkeyDefinition.SpaceVirtualKey, controlDown: true, altDown: false, shiftDown: true, winDown: true, out var displayText);

        Assert.True(captured);
        Assert.Equal("Ctrl+Shift+Win+Space", displayText);
    }
}

public sealed class HotkeyStartupLifecycleTests
{
    [Fact]
    public void Initial_hotkey_conflict_is_nonfatal_and_does_not_change_configuration()
    {
        var registration = new HotkeyRegistration(_ => false, () => true);

        Assert.False(registration.TryRegister("Alt+Space", out var error));
        Assert.True(HotkeyStartupLifecycle.ShouldContinueAfterInitialRegistrationFailure);
        Assert.Equal("The configured Quick Search hotkey is unavailable. Choose another hotkey in Settings.", HotkeyStartupLifecycle.UnavailableMessage);
        Assert.Contains("unavailable", error, StringComparison.OrdinalIgnoreCase);
        Assert.False(registration.IsRegistered);
        Assert.Equal(default, registration.Registered);
        Assert.True(HotkeyDefinition.TryParse("Alt+Space", out _));
    }

    [Fact]
    public void Failed_change_restores_previous_hotkey_and_later_success_registers_the_requested_hotkey()
    {
        var attempts = new Queue<bool>([true, false, true, true]);
        var registration = new HotkeyRegistration(_ => attempts.Dequeue(), () => true);

        Assert.True(registration.TryRegister("Ctrl+Alt+Space", out _));
        Assert.False(registration.TryRegister("Alt+Space", out var error));
        Assert.Contains("previous Quail hotkey remains active", error, StringComparison.Ordinal);
        Assert.True(registration.IsRegistered);
        Assert.Equal("Ctrl+Alt+Space", registration.Registered.DisplayText);

        Assert.True(registration.TryRegister("Alt+Space", out _));
        Assert.True(registration.IsRegistered);
        Assert.Equal("Alt+Space", registration.Registered.DisplayText);
    }
}
