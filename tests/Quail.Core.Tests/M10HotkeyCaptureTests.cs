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
