using Quail.App;

namespace Quail.Core.Tests;

public sealed class M10TrayCallbackTests
{
    [Fact]
    public void DecodeVersion4_UsesLowWordForNotificationAndHighWordForIconId()
    {
        var callback = TrayCallbackDecoder.DecodeVersion4(
            (nint)((800 << 16) | 1024),
            (nint)((1 << 16) | NativeMethods.NinSelect));

        Assert.Equal(TrayCallbackAction.Show, callback.Action);
        Assert.Equal(NativeMethods.NinSelect, callback.Notification);
        Assert.Equal(1u, callback.IconId);
        Assert.Equal(1024, callback.Anchor!.Value.X);
        Assert.Equal(800, callback.Anchor!.Value.Y);
    }

    [Fact]
    public void DecodeVersion4_RecognizesContextMenuWithoutTreatingWholeLParamAsMessage()
    {
        var callback = TrayCallbackDecoder.DecodeVersion4(
            (nint)((40 << 16) | 20),
            (nint)((1 << 16) | NativeMethods.WmContextMenu));

        Assert.Equal(TrayCallbackAction.OpenMenu, callback.Action);
        Assert.Equal(NativeMethods.WmContextMenu, callback.Notification);
        Assert.Equal(1u, callback.IconId);
        Assert.Equal(20, callback.Anchor!.Value.X);
        Assert.Equal(40, callback.Anchor!.Value.Y);
    }

    [Fact]
    public void DecodeVersion4_PreservesNegativeAnchorCoordinates()
    {
        var callback = TrayCallbackDecoder.DecodeVersion4(
            (nint)((unchecked((ushort)-40) << 16) | unchecked((ushort)-20)),
            (nint)((1 << 16) | NativeMethods.WmContextMenu));

        Assert.Equal(-20, callback.Anchor!.Value.X);
        Assert.Equal(-40, callback.Anchor!.Value.Y);
    }

    [Fact]
    public void DecodeLegacy_UsesTheWholeLParamAsTheMouseMessage()
    {
        var callback = TrayCallbackDecoder.DecodeLegacy((nint)NativeMethods.WmRButtonUp);

        Assert.Equal(TrayCallbackAction.OpenMenu, callback.Action);
        Assert.Equal(NativeMethods.WmRButtonUp, callback.Notification);
        Assert.Equal(0u, callback.IconId);
        Assert.Null(callback.Anchor);
    }

    [Fact]
    public void IsUnambiguousLegacyShape_RecognizesTheObservedShellCallback()
    {
        var legacyShape = TrayCallbackDecoder.IsUnambiguousLegacyShape(
            (nint)1,
            (nint)NativeMethods.WmMouseMove,
            iconId: 1);

        Assert.True(legacyShape);
    }

    [Fact]
    public void IsUnambiguousLegacyShape_DoesNotMisclassifyVersion4Callback()
    {
        var version4Shape = TrayCallbackDecoder.IsUnambiguousLegacyShape(
            (nint)((400 << 16) | 300),
            (nint)((1 << 16) | NativeMethods.WmContextMenu),
            iconId: 1);

        Assert.False(version4Shape);
    }
}
