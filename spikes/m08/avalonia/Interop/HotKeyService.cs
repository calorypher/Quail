using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace Quail.M08.Avalonia.Interop;

internal sealed class HotKeyService : IDisposable
{
    private const int HotKeyId = 0x4D08;
    private const uint ModControl = 0x0002;
    private const uint ModAlt = 0x0001;
    private const uint VkSpace = 0x20;
    private const uint WmHotKey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint WmClose = 0x0010;
    private const int ErrorHotKeyAlreadyRegistered = 1409;

    private readonly Action _summon;
    private readonly ManualResetEventSlim _ready = new();
    private readonly Thread _thread;
    private Exception? _startupFailure;
    private uint _threadId;
    private bool _disposed;

    public HotKeyService(Action summon)
    {
        _summon = summon;
        _thread = new Thread(MessageLoop)
        {
            IsBackground = true,
            Name = "Quail M08 global hotkey"
        };
    }

    public void Start()
    {
        _thread.Start();
        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Timed out while registering the M08 global hotkey.");
        }

        if (_startupFailure is not null)
        {
            throw new InvalidOperationException("Could not register Ctrl+Alt+Space.", _startupFailure);
        }
    }

    private void MessageLoop()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _ = PeekMessage(out _, nint.Zero, 0, 0, 0);
            if (!RegisterHotKey(nint.Zero, HotKeyId, ModControl | ModAlt, VkSpace))
            {
                var error = Marshal.GetLastWin32Error();
                var detail = error == ErrorHotKeyAlreadyRegistered
                    ? "Ctrl+Alt+Space is already registered by another application."
                    : new Win32Exception(error).Message;
                throw new InvalidOperationException(detail);
            }

            _ready.Set();
            while (GetMessage(out var message, nint.Zero, 0, 0) > 0)
            {
                if (message.Message == WmHotKey && (nint)message.WParam == HotKeyId)
                {
                    Dispatcher.UIThread.Post(_summon, DispatcherPriority.Normal);
                    continue;
                }

                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupFailure = exception;
            _ready.Set();
        }
        finally
        {
            UnregisterHotKey(nint.Zero, HotKeyId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_threadId != 0)
        {
            PostThreadMessage(_threadId, WmQuit, nint.Zero, nint.Zero);
        }

        _thread.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        internal nint Hwnd;
        internal uint Message;
        internal nuint WParam;
        internal nint LParam;
        internal uint Time;
        internal NativeMethods.POINT Pt;
        internal uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, nint hwnd, uint minFilter, uint maxFilter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, nint hwnd, uint minFilter, uint maxFilter, uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
