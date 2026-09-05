using System.Text.Json;

namespace Quail.Core.Tests;

public sealed class M18RelevanceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Quail-M18-" + Guid.NewGuid());
    private static readonly FileSearchRankingContext Context = new(@"X:\Users\Aster");

    [Fact]
    public void Mandatory_relevance_orders_and_metrics()
    {
        Directory.CreateDirectory(_directory);
        var reports = new List<object>();
        var failures = new List<string>();
        var reciprocalRanks = new List<double>();
        var top1 = 0;
        var top5 = 0;
        foreach (var fixture in Cases())
        {
            var stores = fixture.Entries.GroupBy(entry => entry.Path[..3])
                .Select((group, index) => Build(fixture.Id + index, group.Key, group.ToArray())).ToArray();
            var query = new FileSearchQuery(fixture.Query, Limit: fixture.Limit);
            var actualResults = MultiIndexSearch.Search(stores, query, Context);
            var actual = actualResults.Select(result => result.Result.FullPath!).ToArray();
            var repeated = MultiIndexSearch.Search(stores.Reverse(), query, Context);
            Assert.Equal(actual, repeated.Select(result => result.Result.FullPath));
            var rank = Array.IndexOf(actual, fixture.Expected[0]) + 1;
            if (rank == 1) top1++;
            if (rank is > 0 and <= 5) top5++;
            reciprocalRanks.Add(rank == 0 ? 0 : 1d / rank);
            var orderPass = actual.Take(fixture.Expected.Length).SequenceEqual(fixture.Expected);
            if (!orderPass) failures.Add(fixture.Id);
            reports.Add(new
            {
                id = fixture.Id, query = fixture.Query, limit = fixture.Limit,
                expectedOrder = fixture.Expected, actualOrder = actual,
                preferredRank = rank == 0 ? (int?)null : rank,
                expectedOrderPass = orderPass, deterministicPass = true
            });
        }

        var output = Environment.GetEnvironmentVariable("QUAIL_M18_RELEVANCE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                productionBaseline = Environment.GetEnvironmentVariable("QUAIL_M18_BASELINE") == "1",
                caseCount = reports.Count,
                top1Success = top1, top5Success = top5,
                top1Rate = (double)top1 / reports.Count, top5Rate = (double)top5 / reports.Count,
                mrr = reciprocalRanks.Average(), mandatoryFailures = failures, cases = reports
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        }

        // The opt-in capture runs before production edits. Normal test runs
        // always enforce every explicit order, regardless of aggregate metrics.
        if (Environment.GetEnvironmentVariable("QUAIL_M18_BASELINE") != "1")
        {
            Assert.True(failures.Count == 0, "Mandatory order failures: " + string.Join(", ", failures));
        }
    }

    private static IEnumerable<Fixture> Cases()
    {
        foreach (var query in new[] { "q", "qx", "quartz" })
        {
            string Home(string name) => @"X:\Users\Aster\Work\" + name;
            string Root(string name) => @"X:\" + name;
            var text = new[] { query, query + "-draft", "item-" + query, "sub" + query };
            yield return new Fixture("text-" + query, query, text.Select(name => new Entry(Home(name))).ToArray(), text.Select(Home).ToArray());

            var noise = Enumerable.Range(0, 75).Select(index => new Entry(Root($"{query}-{index:D3}"))).ToArray();
            var preferred = Home(query + "-zzz");
            yield return new Fixture("late-same-tier-" + query, query,
                noise.Append(new Entry(preferred)).ToArray(),
                [preferred, Root(query + "-000"), Root(query + "-001"), Root(query + "-002"), Root(query + "-003")], 5);

            var substrings = Enumerable.Range(0, 6).Select(index => new Entry(Home("sub" + query + index))).ToArray();
            yield return new Fixture("visible-exact-over-user-substring-" + query, query,
                substrings.Append(new Entry(Root(query))).ToArray(),
                [Root(query), .. substrings.Select(entry => entry.Path)]);

            yield return new Fixture("visible-prefix-over-user-substring-" + query, query,
                [new(Home("sub" + query)), new(@"Y:\Work\" + query + "-draft")],
                [@"Y:\Work\" + query + "-draft", Home("sub" + query)]);
        }

        var locations = new[]
        {
            new Entry(@"X:\Users\Aster\Work\config.json"),
            new Entry(@"X:\Users\Birch\Work\config.json"),
            new Entry(@"Y:\Work\config.json"),
            new Entry(@"X:\Users\Aster\AppData\Cache\config.json"),
            new Entry(@"X:\Users\Birch\AppData\Cache\config.json"),
            new Entry(@"Y:\Cache\config.json", 2),
            new Entry(@"X:\Windows\Cache\config.json")
        };
        yield return new Fixture("duplicate-location-bands", "config.json", locations, locations.Select(entry => entry.Path).ToArray());
        yield return new Fixture("visible-substring-over-internal-exact", "quartz",
            [new(@"Y:\Work\subquartz"), new(@"X:\Users\Aster\AppData\quartz"), new(@"Y:\Cache\quartz", 4), new(@"X:\ProgramData\quartz")],
            [@"Y:\Work\subquartz", @"X:\Users\Aster\AppData\quartz", @"Y:\Cache\quartz", @"X:\ProgramData\quartz"]);
        yield return new Fixture("near-duplicates", "config.json",
            [new(@"Y:\Work\config.json.bak"), new(@"Y:\Work\old-config.json"), new(@"Y:\Work\subconfig.json"), new(@"Y:\Work\config.json")],
            [@"Y:\Work\config.json", @"Y:\Work\config.json.bak", @"Y:\Work\old-config.json", @"Y:\Work\subconfig.json"]);
        yield return new Fixture("duplicate-depth-and-length", "README.md",
            [new(@"Y:\Longer\README.md"), new(@"Y:\B\Deep\README.md"), new(@"Y:\B\README.md"), new(@"Y:\A\README.md")],
            [@"Y:\A\README.md", @"Y:\B\README.md", @"Y:\Longer\README.md", @"Y:\B\Deep\README.md"]);
        yield return new Fixture("late-depth-other-volume", "quartz",
            [.. Enumerable.Range(0, 75).Select(index => new Entry(@"Y:\Deep\Tree\" + $"quartz-{index:D3}")), new(@"Y:\quartz-zzz")],
            [@"Y:\quartz-zzz"], 1);
        yield return new Fixture("multi-index-local-truncation", "quartz",
            [.. Enumerable.Range(0, 75).Select(index => new Entry(@"X:\Windows\" + $"quartz-{index:D3}")), new(@"X:\Users\Aster\Work\quartz-zzz"), new(@"Y:\Work\quartz-aaa")],
            [@"X:\Users\Aster\Work\quartz-zzz"], 1);
        yield return new Fixture("multi-index-identical-names", "README.md",
            [new(@"Y:\Work\README.md"), new(@"X:\Work\README.md")],
            [@"X:\Work\README.md", @"Y:\Work\README.md"]);
        yield return new Fixture("final-name-and-path-tie", ".txt",
            [new(@"Y:\B\a.txt"), new(@"Y:\A\a.txt"), new(@"Y:\A\A.txt")],
            [@"Y:\A\A.txt", @"Y:\A\a.txt", @"Y:\B\a.txt"]);
    }

    private IndexStore Build(string id, string mount, Entry[] entries)
    {
        var store = new IndexStore(Path.Combine(_directory, id + ".db"));
        store.BuildFromRecords(new VolumeDescriptor(id, mount, "NTFS", "Synthetic M18"), sink =>
        {
            var ids = new Dictionary<string, NativeFileId>(StringComparer.Ordinal) { [mount.TrimEnd('\\')] = Id(1) };
            sink(new NamespaceRecord(Id(1), Id(1), "", 16, 0, 2));
            foreach (var entry in entries)
            {
                var parts = entry.Path[3..].Split('\\');
                var path = mount.TrimEnd('\\');
                for (var index = 0; index < parts.Length; index++)
                {
                    var parent = ids[path];
                    path += "\\" + parts[index];
                    if (ids.ContainsKey(path)) continue;
                    var fileId = Id(ids.Count + 1);
                    ids.Add(path, fileId);
                    sink(new NamespaceRecord(fileId, parent, parts[index], index == parts.Length - 1 ? entry.Attributes : 16, 0, 2));
                }
            }
        }, checkpoint: new IncrementalCheckpoint(1, 2, 0, 0));
        return store;
    }

    private static NativeFileId Id(int value) => new(BitConverter.GetBytes((long)value));
    private sealed record Entry(string Path, uint Attributes = 0);
    private sealed record Fixture(string Id, string Query, Entry[] Entries, string[] Expected, int Limit = 50);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
