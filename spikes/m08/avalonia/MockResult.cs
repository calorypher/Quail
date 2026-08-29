using System.Text.Json;

namespace Quail.M08.Avalonia;

internal sealed record MockResult(
    string Kind,
    string Name,
    string Path,
    string Extension,
    long? SizeBytes,
    DateTimeOffset ModifiedUtc)
{
    public static IReadOnlyList<MockResult> Load()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "mock-results.json");
        using var document = File.OpenRead(path);
        return JsonSerializer.Deserialize<List<MockResult>>(document, JsonOptions)
            ?? throw new InvalidDataException("The M08 mock result dataset is empty.");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
