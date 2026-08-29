using System.Runtime.InteropServices;

namespace Quail.M08.Avalonia.Interop;

internal static class NativeMethods
{
    internal const uint MonitorDefaultToNearest = 2;
    internal const int SwShow = 5;

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(nint hwnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO monitorInfo);

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hwnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll")]
    internal static extern nint GetFocus();

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool DestroyIcon(nint hIcon);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmFlush();

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(nint monitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        out SHFILEINFO fileInfo,
        uint fileInfoSize,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        internal uint Size;
        internal RECT Monitor;
        internal RECT Work;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct SHFILEINFO
    {
        internal nint HIcon;
        internal int IconIndex;
        internal uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    internal enum MonitorDpiType
    {
        EffectiveDpi = 0
    }
}
