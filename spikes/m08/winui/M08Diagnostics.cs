namespace Quail.M08.WinUi;

internal static class M08Diagnostics
{
    public static string? PathFromArguments(IEnumerable<string> arguments)
    {
        var values = arguments.ToArray();
        var index = Array.IndexOf(values, "--m08-diagnostics");
        return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
    }

    public static void WriteMessage(string? path, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostic logging must never replace the original startup failure.
        }
    }

    public static void Write(string? path, Exception exception)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            WriteMessage(path, exception.ToString());
        }
        catch
        {
            // Diagnostic logging must never replace the original startup failure.
        }
    }
}
