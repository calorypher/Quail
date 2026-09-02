using Quail.App;

namespace Quail.Core.Tests;

public sealed class SearchPerformanceScenarioTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuailTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_reads_a_single_scenario_definition()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "scenario.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "id": "rapid-typing",
              "sessionKind": "warm-same-session",
              "warmupQueries": ["readme"],
              "queries": ["q", "qu", "qua", "quail"],
              "interQueryDelayMilliseconds": 120
            }
            """);

        var scenario = SearchPerformanceScenario.Load(path);

        Assert.Equal("rapid-typing", scenario.Id);
        Assert.Equal("warm-same-session", scenario.SessionKind.Value);
        Assert.Equal(["readme"], scenario.WarmupQueries);
        Assert.Equal(["q", "qu", "qua", "quail"], scenario.Queries);
        Assert.Equal(120, scenario.InterQueryDelayMilliseconds);
    }

    [Fact]
    public void Load_accepts_a_fresh_process_scenario_without_warmup()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "scenario.json");
        File.WriteAllText(path, """
            {
              "schemaVersion": 1,
              "id": "fresh-process-first-search",
              "sessionKind": "fresh-process-first-search",
              "queries": ["project"]
            }
            """);

        var scenario = SearchPerformanceScenario.Load(path);

        Assert.Empty(scenario.WarmupQueries);
        Assert.Equal("fresh-process-first-search", scenario.SessionKind.Value);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"Uppercase\", \"sessionKind\": \"warm-same-session\", \"queries\": [\"readme\"] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"ordinary-name\", \"sessionKind\": \"warm-same-session\", \"queries\": [] }")]
    [InlineData("{ \"schemaVersion\": 2, \"id\": \"ordinary-name\", \"sessionKind\": \"warm-same-session\", \"queries\": [\"readme\"] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"ordinary-name\", \"sessionKind\": \"warm-same-session\", \"queries\": [\"notes\"] }")]
    [InlineData("{ \"schemaVersion\": 1, \"id\": \"fresh-process-first-search\", \"sessionKind\": \"fresh-process-first-search\", \"warmupQueries\": [\"readme\"], \"queries\": [\"project\"] }")]
    public void Load_rejects_invalid_definitions(string content)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "scenario.json");
        File.WriteAllText(path, content);

        Assert.Throws<ArgumentException>(() => SearchPerformanceScenario.Load(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
