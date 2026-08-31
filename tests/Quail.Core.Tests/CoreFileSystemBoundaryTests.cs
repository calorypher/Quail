namespace Quail.Core.Tests;

public sealed class CoreFileSystemBoundaryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuailTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Core_projects_filesystem_results_and_opens_by_opaque_action()
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
        var fileSystem = new FileSystemSearchService(() => [databasePath], opener);
        var core = new FileSearchApplicationService(fileSystem);

        var result = Assert.Single(core.Search(new SearchRequest("report")));

        Assert.Equal("boundary-report.txt", result.Name);
        Assert.Equal("X:\\boundary-report.txt", result.FullPath);
        Assert.DoesNotContain(
            typeof(SearchResult).GetProperties(),
            property => property.PropertyType.Assembly == typeof(NativeFileId).Assembly);
        Assert.Empty(typeof(SearchResultAction).GetProperties());

        _ = core.Search(new SearchRequest("boundary"));
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
}
