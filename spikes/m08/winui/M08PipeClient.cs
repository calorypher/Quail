using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Quail.M08.WinUi;

public sealed class M08PipeClient : IDisposable
{
    private readonly NamedPipeClientStream? _pipe;
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public M08PipeClient(string? pipeName)
    {
        if (!string.IsNullOrWhiteSpace(pipeName))
        {
            _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        }
    }

    public async Task ConnectAsync()
    {
        if (_pipe is null)
        {
            return;
        }

        await _pipe.ConnectAsync(5000);
        _writer = new StreamWriter(_pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
    }

    public void Emit(object message)
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
