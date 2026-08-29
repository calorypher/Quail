using System.Runtime.InteropServices;

namespace Quail.App;

internal static class NativeMethods
{
    internal const uint WmNull = 0x0000;
    internal const uint WmHotKey = 0x0312;
    internal const uint WmMouseMove = 0x0200;
    internal const uint WmUser = 0x0400;
    internal const uint WmApp = 0x8000;
    internal const uint WmRButtonUp = 0x0205;
    internal const uint WmLButtonUp = 0x0202;
    internal const uint WmContextMenu = 0x007B;
    internal const uint NinSelect = WmUser;
    internal const uint NinKeySelect = WmUser + 1;
    internal const uint NimAdd = 0x00000000;
    internal const uint NimDelete = 0x00000002;
    internal const uint NimSetVersion = 0x00000004;
    internal const uint NifMessage = 0x00000001;
    internal const uint NifIcon = 0x00000002;
    internal const uint NifTip = 0x00000004;
    internal const uint NotifyIconVersion4 = 4;
    internal const uint TpmRightButton = 0x0002;
    internal const uint TpmReturnCmd = 0x0100;
    internal const uint MfString = 0x0000;
    internal const uint MfSeparator = 0x0800;
    internal const int SwHide = 0;
    internal const int SwShow = 5;
    internal const uint MonitorDefaultToNearest = 2;
    internal const int GwlWndProc = -4;
    internal const int GwlStyle = -16;
    internal const int GwlExStyle = -20;
    internal const nint WsDlgFrame = 0x00400000;
    internal const nint WsExToolWindow = 0x00000080;
    internal const nint WsExAppWindow = 0x00040000;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoMove = 0x0002;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpFrameChanged = 0x0020;
    internal const uint DwmwaBorderColor = 34;
    internal const uint DwmwaUseImmersiveDarkMode = 20;
    internal const uint DwmColorNone = 0xFFFFFFFE;
    internal const uint WmSetIcon = 0x0080;
    internal const nint IconSmall = 0;
    internal const nint IconBig = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetFocus();

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo monitorInfo);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hWnd, out Rect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint SendMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    internal static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    internal static extern nint SetWindowLongPtr(nint hWnd, int index, nint newLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("kernel32.dll")]
    internal static extern void SetLastError(uint error);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowSubclass(nint hWnd, SubclassProc callback, nuint idSubclass, nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc callback, nuint idSubclass);

    [DllImport("comctl32.dll")]
    internal static extern nint DefSubclassProc(nint hWnd, uint message, nint wParam, nint lParam);

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool AppendMenu(nint menu, uint flags, nuint itemId, string? text);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint hWnd, nint reserved);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyMenu(nint menu);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(nint hWnd, uint attribute, ref uint value, uint valueSize);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate nint SubclassProc(nint hWnd, uint message, nint wParam, nint lParam, nuint idSubclass, nuint referenceData);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X; internal int Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { internal int Left; internal int Top; internal int Right; internal int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo { internal uint Size; internal Rect Monitor; internal Rect Work; internal uint Flags; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct NotifyIconData
    {
        internal uint Size;
        internal nint WindowHandle;
        internal uint Id;
        internal uint Flags;
        internal uint CallbackMessage;
        internal nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string Tip;
        internal uint State;
        internal uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string Info;
        internal uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string InfoTitle;
        internal uint InfoFlags;
        internal Guid GuidItem;
        internal nint BalloonIcon;
    }

    internal static int NotifyIconDataSize => Marshal.SizeOf<NotifyIconData>();

    internal static int NotifyIconDataVersionOffset => checked((int)Marshal.OffsetOf<NotifyIconData>(nameof(NotifyIconData.TimeoutOrVersion)));
}
