namespace Quail.App;

internal enum TrayCallbackAction
{
    None,
    Show,
    OpenMenu
}

internal readonly record struct TrayCallback(uint Notification, TrayCallbackAction Action, uint IconId, NativeMethods.Point? Anchor);

internal static class TrayCallbackDecoder
{
    public static TrayCallback DecodeVersion4(nint wParam, nint lParam)
    {
        var notification = LowWord(lParam);
        var iconId = HighWord(lParam);
        var anchor = new NativeMethods.Point { X = SignedLowWord(wParam), Y = SignedHighWord(wParam) };
        return new TrayCallback(notification, ToAction(notification), iconId, anchor);
    }

    public static TrayCallback DecodeLegacy(nint lParam)
    {
        var notification = unchecked((uint)(long)lParam);
        return new TrayCallback(notification, ToAction(notification), 0, null);
    }

    public static bool IsUnambiguousLegacyShape(nint wParam, nint lParam, uint iconId)
    {
        return unchecked((uint)(long)wParam) == iconId &&
            HighWord(lParam) == 0 &&
            IsKnownLegacyNotification(unchecked((uint)(long)lParam));
    }

    private static TrayCallbackAction ToAction(uint notification)
    {
        return notification switch
        {
            NativeMethods.WmLButtonUp or NativeMethods.NinSelect or NativeMethods.NinKeySelect => TrayCallbackAction.Show,
            NativeMethods.WmRButtonUp or NativeMethods.WmContextMenu => TrayCallbackAction.OpenMenu,
            _ => TrayCallbackAction.None
        };
    }

    private static bool IsKnownLegacyNotification(uint notification)
    {
        return notification is NativeMethods.WmMouseMove or
            NativeMethods.WmLButtonUp or
            NativeMethods.WmRButtonUp or
            NativeMethods.WmContextMenu or
            NativeMethods.NinSelect or
            NativeMethods.NinKeySelect;
    }

    private static ushort LowWord(nint value) => unchecked((ushort)(long)value);

    private static ushort HighWord(nint value) => unchecked((ushort)((long)value >> 16));

    private static short SignedLowWord(nint value) => unchecked((short)(long)value);

    private static short SignedHighWord(nint value) => unchecked((short)((long)value >> 16));
}
