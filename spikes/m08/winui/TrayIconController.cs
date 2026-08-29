using System.Runtime.InteropServices;

namespace Quail.M08.WinUi;

internal sealed class TrayIconController : IDisposable
{
    private const uint IconId = 1;
    private const uint CallbackMessage = NativeMethods.WmApp + 8;
    private const uint ShowCommand = 1001;
    private const uint ExitCommand = 1002;
    private readonly nint _windowHandle;
    private readonly nint _icon;
    private bool _added;

    public TrayIconController(nint windowHandle)
    {
        _windowHandle = windowHandle;
        _icon = NativeMethods.LoadIcon(0, NativeMethods.IdIApplication);
        if (_icon == 0)
        {
            throw new InvalidOperationException($"LoadIcon failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Add()
    {
        var data = CreateData(NativeMethods.NifMessage | NativeMethods.NifIcon | NativeMethods.NifTip);
        if (!NativeMethods.Shell_NotifyIcon(NativeMethods.NimAdd, ref data))
        {
            throw new InvalidOperationException($"Shell_NotifyIcon(NIM_ADD) failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }

        data.TimeoutOrVersion = NativeMethods.NotifyIconVersion4;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NimSetVersion, ref data);
        _added = true;
    }

    public bool HandleMessage(uint message, nint wParam, nint lParam, Action show, Action exit)
    {
        if (message != CallbackMessage)
        {
            return false;
        }

        var mouseMessage = unchecked((uint)(long)lParam);
        if (mouseMessage is NativeMethods.WmLButtonUp)
        {
            show();
            return true;
        }

        if (mouseMessage is NativeMethods.WmRButtonUp or NativeMethods.WmContextMenu)
        {
            ShowMenu(show, exit);
            return true;
        }

        return true;
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = CreateData(0);
            NativeMethods.Shell_NotifyIcon(NativeMethods.NimDelete, ref data);
            _added = false;
        }
    }

    private void ShowMenu(Action show, Action exit)
    {
        var menu = NativeMethods.CreatePopupMenu();
        if (menu == 0)
        {
            return;
        }

        try
        {
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ShowCommand, "Show");
            NativeMethods.AppendMenu(menu, NativeMethods.MfSeparator, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MfString, ExitCommand, "Exit");
            NativeMethods.GetCursorPos(out var point);
            NativeMethods.SetForegroundWindow(_windowHandle);
            var command = NativeMethods.TrackPopupMenuEx(menu, NativeMethods.TpmRightButton | NativeMethods.TpmReturnCmd, point.X, point.Y, _windowHandle, 0);
            if (command == ShowCommand)
            {
                show();
            }
            else if (command == ExitCommand)
            {
                exit();
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private NativeMethods.NotifyIconData CreateData(uint flags)
    {
        return new NativeMethods.NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = IconId,
            Flags = flags,
            CallbackMessage = CallbackMessage,
            Icon = _icon,
            Tip = "Quail M08 WinUI prototype"
        };
    }
}
