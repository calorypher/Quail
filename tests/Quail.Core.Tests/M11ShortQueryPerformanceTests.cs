using System.Diagnostics;
using System.Text.Json;
using Quail.App;
using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M11ShortQueryPerformanceTests
{
    private const int EntryCount = 850_000;
    private const int MeasurementTrials = 7;
    private const int RapidTypingTrials = 5;

    [Fact]
    public async Task Measure_current_schema_short_query_fallback_when_explicitly_requested()
    {
        var output = Environment.GetEnvironmentVariable("QUAIL_M11_PERFORMANCE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        var outputDirectory = Path.GetFullPath(output);
        Directory.CreateDirectory(outputDirectory);
        var store = BuildFixture(outputDirectory);
        var measurements = new Dictionary<string, double>
        {
            ["oneCharacterBroadMedianMs"] = MeasureMedian(store, "a"),
            ["twoCharacterBroadMedianMs"] = MeasureMedian(store, "ab"),
            ["threeCharacterBroadMedianMs"] = MeasureMedian(store, "abc"),
            ["selectiveMedianMs"] = MeasureMedian(store, "selective-needle"),
            ["zeroResultMedianMs"] = MeasureMedian(store, "qzq"),
        };
        var searchServiceMeasurements = new Dictionary<string, double>
        {
            ["oneCharacterBroadMedianMs"] = MeasureMultiIndexMedian(store, "a"),
            ["twoCharacterBroadMedianMs"] = MeasureMultiIndexMedian(store, "ab"),
            ["threeCharacterBroadMedianMs"] = MeasureMultiIndexMedian(store, "abc"),
        };

        var rapidFinalMilliseconds = await MeasureRapidFinalQueryAsync(store);
        var deferredRapidFinalMilliseconds = await MeasureDeferredRapidFinalQueryAsync(store);
        var summary = new
        {
            entryCount = EntryCount,
            core = measurements,
            searchService = searchServiceMeasurements,
            rapidTypingBeforeShortQueryDeferMilliseconds = rapidFinalMilliseconds,
            rapidTypingWithShortQueryDeferMilliseconds = deferredRapidFinalMilliseconds,
            note = "The before measurement completes abc after an already-started a direct fallback. The deferred measurement schedules a and ab inside the 150 ms window, cancels them when abc arrives, and measures only the immediate abc coordinator completion.",
        };
        await File.WriteAllTextAsync(
            Path.Combine(outputDirectory, "short-query-performance.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(measurements.Values.All(value => value >= 0));
        Assert.True(searchServiceMeasurements.Values.All(value => value >= 0));
        Assert.True(rapidFinalMilliseconds >= 0);
        Assert.True(deferredRapidFinalMilliseconds >= 0);
    }

    private static IndexStore BuildFixture(string outputDirectory)
    {
        var databasePath = Path.Combine(outputDirectory, "m11-short-query-performance.db");
        var root = Id(1);
        var store = new IndexStore(databasePath);
        store.BuildFromRecords(
            new VolumeDescriptor("m11-short-query-performance", "X:\\", "NTFS", "M11 performance fixture"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, "", 16, 0, 2));
                for (var index = 2; index <= EntryCount + 1; index++)
                {
                    var name = index == EntryCount + 1
                        ? "selective-needle.txt"
                        : (index % 5) switch
                        {
                            0 => $"alpha-entry-{index:D6}.txt",
                            1 => $"about-entry-{index:D6}.txt",
                            2 => $"abc-entry-{index:D6}.txt",
                            3 => $"archive-entry-{index:D6}.txt",
                            _ => $"data-entry-{index:D6}.txt",
                        };
                    sink(new NamespaceRecord(Id(index), root, name, 0, 0, 2));
                }
            });
        return store;
    }

    private static double MeasureMedian(IndexStore store, string query)
    {
        _ = store.Search(new FileSearchQuery(query));
        var timings = new List<double>();
        for (var iteration = 0; iteration < MeasurementTrials; iteration++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = store.Search(new FileSearchQuery(query));
            timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        timings.Sort();
        return timings[timings.Count / 2];
    }

    private static double MeasureMultiIndexMedian(IndexStore store, string query)
    {
        _ = MultiIndexSearch.Search([store], new FileSearchQuery(query));
        var timings = new List<double>();
        for (var iteration = 0; iteration < MeasurementTrials; iteration++)
        {
            var start = Stopwatch.GetTimestamp();
            _ = MultiIndexSearch.Search([store], new FileSearchQuery(query));
            timings.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        timings.Sort();
        return timings[timings.Count / 2];
    }

    private static Task<double> MeasureRapidFinalQueryAsync(IndexStore store)
    {
        return MeasureMedianAsync(() => MeasureRapidFinalQueryOnceAsync(store));
    }

    private static async Task<double> MeasureRapidFinalQueryOnceAsync(IndexStore store)
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        var finalCompletion = new TaskCompletionSource<SearchCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new LatestSearchCoordinator(query =>
        {
            if (query == "a")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
            }

            return Project(MultiIndexSearch.Search([store], new FileSearchQuery(query)));
        });
        coordinator.Completed += completion =>
        {
            if (completion.IsCurrent && completion.Results is not null && completion.Results.Count > 0 && completion.Results[0].Title.StartsWith("abc-", StringComparison.Ordinal))
            {
                finalCompletion.TrySetResult(completion);
            }
        };

        coordinator.Request("a");
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        var start = Stopwatch.GetTimestamp();
        coordinator.Request("ab");
        coordinator.Request("abc");
        releaseFirst.Set();
        _ = await finalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private static Task<double> MeasureDeferredRapidFinalQueryAsync(IndexStore store)
    {
        return MeasureMedianAsync(() => MeasureDeferredRapidFinalQueryOnceAsync(store));
    }

    private static async Task<double> MeasureDeferredRapidFinalQueryOnceAsync(IndexStore store)
    {
        var shortQueriesExecuted = 0;
        using var deferrer = new ShortQueryDeferrer(
            TimeSpan.FromMilliseconds(150),
            (_, _) => Interlocked.Increment(ref shortQueriesExecuted));
        using var coordinator = new LatestSearchCoordinator(query => Project(MultiIndexSearch.Search([store], new FileSearchQuery(query))));
        var finalCompletion = new TaskCompletionSource<SearchCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Completed += completion =>
        {
            if (completion.IsCurrent && completion.Results is not null && completion.Results.Count > 0 && completion.Results[0].Title.StartsWith("abc-", StringComparison.Ordinal))
            {
                finalCompletion.TrySetResult(completion);
            }
        };

        deferrer.Schedule(1, "a");
        deferrer.Schedule(2, "ab");
        deferrer.Cancel();
        var start = Stopwatch.GetTimestamp();
        coordinator.Request("abc");
        _ = await finalCompletion.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        await Task.Delay(200);
        Assert.Equal(0, Volatile.Read(ref shortQueriesExecuted));
        return elapsed;
    }

    private static async Task<double> MeasureMedianAsync(Func<Task<double>> measure)
    {
        var timings = new List<double>();
        for (var iteration = 0; iteration < RapidTypingTrials; iteration++)
        {
            timings.Add(await measure());
        }

        timings.Sort();
        return timings[timings.Count / 2];
    }

    private static NativeFileId Id(int value)
    {
        return new NativeFileId(BitConverter.GetBytes((long)value));
    }

    private static IReadOnlyList<SearchResult> Project(IReadOnlyList<IndexedFileSearchResult> results)
    {
        return results.Select(result => new SearchResult(
            new SearchResultAction(),
            result.Result.Name,
            result.Result.FullPath,
            result.Result.IsDirectory ? "Folder" : "File",
            result.Result.IsDirectory ? "Folder" : result.Result.Extension?.ToUpperInvariant() ?? "File",
            result.Result.IsDirectory ? "folder" : result.Result.Extension?.ToUpperInvariant() ?? "file",
            result.Result.IsDirectory ? "\uE8B7" : "\uE8A5")).ToArray();
    }
}
