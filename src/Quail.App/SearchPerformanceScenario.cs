using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quail.App;

internal sealed record SearchPerformanceScenario(
    string Id,
    SearchPerformanceSessionKind SessionKind,
    IReadOnlyList<string> WarmupQueries,
    IReadOnlyList<string> Queries,
    int InterQueryDelayMilliseconds)
{
    private static readonly Regex IdPattern = new("^[a-z0-9][a-z0-9-]*$", RegexOptions.CultureInvariant);

    public static SearchPerformanceScenario Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var document = JsonSerializer.Deserialize<SearchPerformanceScenarioDocument>(
            File.ReadAllText(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new ArgumentException("The search performance scenario file is empty.");

        if (document.SchemaVersion != 1)
        {
            throw new ArgumentException("The search performance scenario schemaVersion must be 1.");
        }

        if (string.IsNullOrWhiteSpace(document.Id) || !IdPattern.IsMatch(document.Id))
        {
            throw new ArgumentException("The search performance scenario id must use lowercase letters, digits, and hyphens.");
        }

        var warmupQueries = ValidateQueries(document.WarmupQueries ?? [], "warmupQueries");
        var queries = ValidateQueries(document.Queries ?? [], "queries");
        if (queries.Count == 0)
        {
            throw new ArgumentException("The search performance scenario requires at least one query.");
        }

        if (document.InterQueryDelayMilliseconds is < 0 or > 10_000)
        {
            throw new ArgumentException("The search performance scenario interQueryDelayMilliseconds must be between 0 and 10000.");
        }

        return new SearchPerformanceScenario(
            document.Id,
            SearchPerformanceSessionKind.Parse(document.SessionKind ?? string.Empty),
            warmupQueries,
            queries,
            document.InterQueryDelayMilliseconds ?? 0);
    }

    private static IReadOnlyList<string> ValidateQueries(IReadOnlyList<string> values, string propertyName)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"The search performance scenario {propertyName} entries must not be empty.");
            }
        }

        return values;
    }

    private sealed record SearchPerformanceScenarioDocument(
        int SchemaVersion,
        string? Id,
        string? SessionKind,
        IReadOnlyList<string>? WarmupQueries,
        IReadOnlyList<string>? Queries,
        int? InterQueryDelayMilliseconds);
}
