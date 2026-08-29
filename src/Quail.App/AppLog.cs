namespace Quail.App;

internal static class AppLog
{
    private const long MaximumBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static string? _path;

    public static void Configure(string? explicitPath)
    {
        _path = explicitPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quail",
            "Logs",
            "quail.log");
    }

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                var path = _path ?? throw new InvalidOperationException("Logging was not configured.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RotateIfNeeded(path);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
                if (exception is not null)
                {
                    WriteExceptionDetails(path, exception);
                }
            }
        }
        catch
        {
            // Logging must never prevent the resident shell from starting or exiting.
        }
    }

    private static void WriteExceptionDetails(string path, Exception exception)
    {
        var depth = 0;
        for (var current = exception; current is not null; current = current.InnerException)
        {
            File.AppendAllText(
                path,
                $"Exception[{depth}].Type: {current.GetType().FullName}{Environment.NewLine}" +
                $"Exception[{depth}].Message: {current.Message}{Environment.NewLine}" +
                $"Exception[{depth}].HRESULT: 0x{current.HResult:X8}{Environment.NewLine}" +
                $"Exception[{depth}].Source: {current.Source ?? "<none>"}{Environment.NewLine}" +
                $"Exception[{depth}].StackTrace:{Environment.NewLine}{current.StackTrace ?? "<none>"}{Environment.NewLine}");
            depth++;
        }

        File.AppendAllText(path, $"Exception.ToString():{Environment.NewLine}{exception}{Environment.NewLine}");
    }

    private static void RotateIfNeeded(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaximumBytes)
        {
            return;
        }

        var previous = path + ".1";
        File.Move(path, previous, overwrite: true);
    }
}
