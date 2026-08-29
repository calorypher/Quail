using System.Runtime.InteropServices;

namespace Quail.App;

internal sealed class TrayIconController : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = NativeMethods.WmApp + 10;
    private const uint ShowCommand = 1001;
    private const uint SettingsCommand = 1002;
    private const uint ExitCommand = 1003;
    private readonly nint _windowHandle;
    private readonly nint _icon = BrandingAssets.CreateTrayIcon();
    private bool _added;
    private bool _usesVersion4;
    private bool _legacyShapeFallbackLogged;

    public TrayIconController(nint windowHandle) => _windowHandle = windowHandle;

    public bool TryAdd()
    {
        var data = CreateData(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip);
        if (!NativeMethods.Shell_NotifyIconW(NativeMethods.NimAdd, ref data))
        {
            AppLog.Write($"Shell_NotifyIcon(NIM_ADD) failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        var versionData = CreateData(0);
        versionData.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        AppLog.Write($"NOTIFYICONDATAW NIM_SETVERSION size={NativeMethods.NotifyIconDataSize} versionOffset={NativeMethods.NotifyIconDataVersionOffset} version={versionData.TimeoutOrVersion}.");
        _usesVersion4 = NativeMethods.Shell_NotifyIconW(NativeMethods.NimSetVersion, ref versionData);
        if (!_usesVersion4)
        {
            AppLog.Write($"Shell_NotifyIcon(NIM_SETVERSION) failed with Win32 error {Marshal.GetLastWin32Error()}; using legacy callback decoding.");
        }

        _added = true;
        return true;
    }

    public bool HandleMessage(uint message, nint wParam, nint lParam, Action show, Action settings, Action exit)
    {
        if (message != CallbackMessage)
        {
            return false;
        }

        var legacyShape = _usesVersion4 && TrayCallbackDecoder.IsUnambiguousLegacyShape(wParam, lParam, IconId);
        if (legacyShape && !_legacyShapeFallbackLogged)
        {
            _legacyShapeFallbackLogged = true;
            AppLog.Write("Tray callback has an unambiguous legacy shape while VERSION_4 is active; decoding this callback as legacy.");
        }

        var callback = _usesVersion4 && !legacyShape
            ? TrayCallbackDecoder.DecodeVersion4(wParam, lParam)
            : TrayCallbackDecoder.DecodeLegacy(lParam);
        var mode = _usesVersion4 && !legacyShape ? "VERSION_4" : legacyShape ? "legacy-fallback" : "legacy";
        AppLog.Write($"Tray callback message=0x{message:X8} wParam=0x{(ulong)wParam:X} lParam=0x{(ulong)lParam:X} mode={mode} notification=0x{callback.Notification:X4} iconId={callback.IconId} action={callback.Action}.");
        if (_usesVersion4 && !legacyShape && callback.IconId != IconId)
        {
            AppLog.Write($"Tray callback ignored because iconId={callback.IconId} does not match Quail iconId={IconId}.");
            return true;
        }

        switch (callback.Action)
        {
            case TrayCallbackAction.Show:
                show();
                break;
            case TrayCallbackAction.OpenMenu:
                ShowMenu(callback.Anchor, show, settings, exit);
                break;
        }

        return true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIconW(NativeMethods.NimDelete, ref data);
            _added = false;
        }
        if (_icon != 0) NativeMethods.DestroyIcon(_icon);
    }

    private void ShowMenu(NativeMethods.Point? anchor, Action show, Action settings, Action exit)
    {
        AppLog.Write($"Tray ShowMenu entered anchor={(anchor.HasValue ? $"{anchor.Value.X},{anchor.Value.Y}" : "cursor")}.");
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            AppLog.Write($"CreatePopupMenu failed with Win32 error {Marshal.GetLastWin32Error()}.");
            return;
        }
        AppLog.Write($"CreatePopupMenu succeeded handle=0x{(ulong)menu:X}.");
        try
        {
            AppendMenuItem(menu, NativeMethods.MfString, ShowCommand, "Show Quick Search");
            AppendMenuItem(menu, NativeMethods.MfString, SettingsCommand, "Settings");
            AppendMenuItem(menu, NativeMethods.MfSeparator, 0, null);
            AppendMenuItem(menu, NativeMethods.MfString, ExitCommand, "Exit");
            if (!anchor.HasValue)
            {
                if (!NativeMethods.GetCursorPos(out var cursor))
                {
                    AppLog.Write($"GetCursorPos failed with Win32 error {Marshal.GetLastWin32Error()}.");
                    return;
                }
                anchor = cursor;
            }

            var point = anchor.Value;
            var foregroundSet = NativeMethods.SetForegroundWindow(_windowHandle);
            AppLog.Write($"SetForegroundWindow result={foregroundSet} error={(foregroundSet ? 0 : Marshal.GetLastWin32Error())}.");
            NativeMethods.SetLastError(0);
            var command = NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd, point.X, point.Y, _windowHandle, 0);
            AppLog.Write($"TrackPopupMenuEx result={command} error={(command == 0 ? Marshal.GetLastWin32Error() : 0)}.");
            switch (command)
            {
                case ShowCommand: show(); break;
                case SettingsCommand: settings(); break;
                case ExitCommand: exit(); break;
            }

            var posted = NativeMethods.PostMessage(_windowHandle, NativeMethods.WmNull, 0, 0);
            AppLog.Write($"PostMessage(WM_NULL) result={posted} error={(posted ? 0 : Marshal.GetLastWin32Error())}.");
        }
        finally { NativeMethods.DestroyMenu(menu); }
    }

    private static void AppendMenuItem(nint menu, uint flags, uint command, string? text)
    {
        var appended = NativeMethods.AppendMenu(menu, flags, command, text);
        AppLog.Write($"AppendMenu text={text ?? "<separator>"} result={appended} error={(appended ? 0 : Marshal.GetLastWin32Error())}.");
    }

    private NativeMethods.NotifyIconData CreateData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(), WindowHandle = _windowHandle, Id = IconId,
        Flags = flags, CallbackMessage = CallbackMessage, Icon = _icon, Tip = "Quail"
    };
}
