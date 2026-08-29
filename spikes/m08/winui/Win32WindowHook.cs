using System.Runtime.InteropServices;

namespace Quail.M08.WinUi;

internal sealed class Win32WindowHook : IDisposable
{
    private readonly nint _windowHandle;
    private readonly NativeMethods.SubclassProc _callback;
    private const nuint SubclassId = 0x4D303857;

    public Win32WindowHook(nint windowHandle, Func<uint, nint, nint, bool> handler)
    {
        _windowHandle = windowHandle;
        _callback = (hWnd, message, wParam, lParam, _, _) =>
        {
            if (handler(message, wParam, lParam))
            {
                return 0;
            }

            return NativeMethods.DefSubclassProc(hWnd, message, wParam, lParam);
        };

        if (!NativeMethods.SetWindowSubclass(_windowHandle, _callback, SubclassId, 0))
        {
            throw new InvalidOperationException($"SetWindowSubclass failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    public void Dispose()
    {
        NativeMethods.RemoveWindowSubclass(_windowHandle, _callback, SubclassId);
    }
}
