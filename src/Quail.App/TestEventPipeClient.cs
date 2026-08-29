using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Quail.App;

internal sealed class TestEventPipeClient : IDisposable
{
    private readonly NamedPipeClientStream? _pipe;
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public TestEventPipeClient(string? pipeName)
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
        _writer = new StreamWriter(_pipe, new UTF8Encoding(false)) { AutoFlush = true };
    }

    public void Emit(object payload)
    {
        if (_writer is null)
        {
            return;
        }

        lock (_gate)
        {
            _writer.WriteLine(JsonSerializer.Serialize(payload));
        }
    }

    public void Dispose()
    {
        _writer?.Dispose();
        _pipe?.Dispose();
    }
}
