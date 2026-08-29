using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Quail.M08.Avalonia;

internal sealed class PipeEventWriter : IDisposable
{
    private readonly NamedPipeClientStream? _pipe;
    private readonly StreamWriter? _writer;
    private readonly object _gate = new();

    public PipeEventWriter(string? pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
        {
            return;
        }

        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
        _pipe.Connect(5000);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1024, leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    public void VisibleReady(nint hwnd, nint focusHwnd, bool queryHasKeyboardFocus, uint windowDpi, int windowLeft, int windowTop, int windowWidth, int windowHeight) => Write(new
    {
        @event = "visible-ready",
        framework = "avalonia",
        hwnd = hwnd.ToInt64(),
        focusHwnd = focusHwnd.ToInt64(),
        queryHasKeyboardFocus,
        windowDpi,
        windowLeft,
        windowTop,
        windowWidth,
        windowHeight
    });

    public void StartupHidden() => Write(new { @event = "startup-hidden" });

    public void SelectionScrollRequested(int index) => Write(new { @event = "selection-scroll-requested", index });

    public void ShellIconsReady() => Write(new { @event = "shell-icons-ready" });

    public void QueryChanged(string query, int resultCount) => Write(new
    {
        @event = "query-changed",
        query,
        resultCount
    });

    public void SelectionChanged(int index, string name) => Write(new
    {
        @event = "selection-changed",
        index,
        name
    });

    public void Confirmed(string name) => Write(new { @event = "confirmed", name });

    public void Hidden() => Write(new { @event = "hidden" });

    private void Write<T>(T message)
    {
        if (_writer is null)
        {
            return;
        }

        lock (_gate)
        {
            _writer.WriteLine(JsonSerializer.Serialize(message));
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
