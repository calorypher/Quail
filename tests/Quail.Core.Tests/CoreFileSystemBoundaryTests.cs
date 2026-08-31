using System.Reflection;

namespace Quail.Core.Tests;

public sealed class CoreFileSystemBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuailTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Repeated_searches_preserve_returned_action_without_a_global_registry()
    {
        Directory.CreateDirectory(_directory);
        var databasePath = Path.Combine(_directory, "boundary.db");
        var root = new NativeFileId(BitConverter.GetBytes(1L));
        var file = new NativeFileId(BitConverter.GetBytes(2L));
        var store = new IndexStore(databasePath);
        store.BuildFromRecords(
            new VolumeDescriptor("boundary-volume", "X:\\", "NTFS", "boundary"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, string.Empty, 16, 0, 2));
                sink(new NamespaceRecord(file, root, "boundary-report.txt", 0, 0, 2));
            });

        string? openedPath = null;
        var opener = new IndexedEntryOpener(
            new RecordingShell(path => openedPath = path),
            _ => true);
        var fileSystem = new FileSystemSearchSource(() => [databasePath], opener);
        var core = new SearchApplicationService([fileSystem]);

        var result = Assert.Single(core.Search(new SearchRequest("report")));

        Assert.Equal("boundary-report.txt", result.Title);
        Assert.Equal("X:\\boundary-report.txt", result.Context);
        Assert.Equal("File", result.Kind);
        Assert.Equal("TXT", result.Metadata);
        Assert.DoesNotContain(
            typeof(SearchResult).GetProperties(),
            property => property.PropertyType.Assembly == typeof(NativeFileId).Assembly);
        Assert.DoesNotContain(typeof(SearchResult).GetProperties(), property => property.Name is "FullPath" or "IsDirectory" or "Extension" or "LogicalSize" or "Attributes" or "LastWriteTimeUtcFileTime");
        Assert.Empty(typeof(SearchResultAction).GetProperties());

        for (var index = 0; index < 64; index++)
        {
            _ = core.Search(new SearchRequest("boundary"));
        }

        Assert.DoesNotContain(
            typeof(SearchApplicationService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => ReferencesFileSystemAction(field.FieldType));
        core.Open(result.Action);

        Assert.Equal("X:\\boundary-report.txt", openedPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingShell(Action<string> open) : IWindowsShellLauncher
    {
        public void Open(string path) => open(path);
    }

    [Fact]
    public void Core_aggregates_fake_sources_and_routes_their_opaque_actions_without_filesystem_references()
    {
        var opened = new List<string>();
        var first = new FakeSearchSource("first", opened, "alpha");
        var second = new FakeSearchSource("second", opened, "beta");
        var core = new SearchApplicationService([first, second]);

        var results = core.Search(new SearchRequest("query"));

        Assert.Equal(["alpha", "beta"], results.Select(result => result.Title));
        Assert.Equal(["alpha"], core.Search(new SearchRequest("query", Limit: 1)).Select(result => result.Title));
        core.Open(results[1].Action);
        Assert.Equal(["second:beta"], opened);
        Assert.DoesNotContain(
            typeof(SearchApplicationService).Assembly.GetReferencedAssemblies(),
            assembly => string.Equals(assembly.Name, "Quail.FileSystem", StringComparison.Ordinal));
    }

    private static bool ReferencesFileSystemAction(Type type)
    {
        return type.Assembly == typeof(FileSystemSearchSource).Assembly ||
            type.GetGenericArguments().Any(ReferencesFileSystemAction);
    }

    private sealed class FakeSearchSource(string name, List<string> opened, string title) : ISearchSource
    {
        public IReadOnlyList<SearchResult> Search(SearchRequest request) =>
        [new SearchResult(
            new SearchResultAction(() => opened.Add($"{name}:{title}")),
            title,
            $"{name} context",
            "Fake result",
            "Fake metadata",
            "fake",
            "\uE8A5")];
    }
}
