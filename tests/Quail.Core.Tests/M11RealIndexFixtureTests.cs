using Quail.Core;

namespace Quail.Core.Tests;

public sealed class M11RealIndexFixtureTests
{
    [Fact]
    public void BuildFromRecords_creates_a_complete_file_backed_fixture_when_explicitly_requested()
    {
        var output = Environment.GetEnvironmentVariable("QUAIL_M11_FIXTURE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;

        var rootPath = Path.GetFullPath(output);
        var dataPath = Path.Combine(rootPath, "data");
        Directory.CreateDirectory(Path.Combine(dataPath, "quail-folder"));
        File.WriteAllText(Path.Combine(dataPath, "quail-alpha.txt"), "M11 host fixture alpha");
        File.WriteAllText(Path.Combine(dataPath, "quail-beta.pdf"), "M11 host fixture beta");
        File.WriteAllText(Path.Combine(dataPath, "quail-gamma.txt"), "M11 host fixture gamma");
        var databasePath = Path.Combine(rootPath, "m11-host-fixture.db");
        var root = Id("0000000000000001");
        var alpha = Id("0000000000000002");
        var beta = Id("0000000000000003");
        var folder = Id("0000000000000004");
        var store = new IndexStore(databasePath);

        store.BuildFromRecords(
            new VolumeDescriptor("m11-controlled-host-fixture", dataPath + Path.DirectorySeparatorChar, "NTFS", "M11 fixture"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, "", 16, 0, 2));
                sink(new NamespaceRecord(alpha, root, "quail-alpha.txt", 0, 0, 2));
                sink(new NamespaceRecord(beta, root, "quail-beta.pdf", 0, 0, 2));
                sink(new NamespaceRecord(folder, root, "quail-folder", 16, 0, 2));
            },
            acquireMetadata: record => record.Name is "quail-alpha.txt" or "quail-beta.pdf"
                ? Metadata(dataPath, record.Name)
                : new FileMetadata(null, null));

        Assert.Equal(IndexState.Complete, store.GetStatus().State);
        Assert.Equal(3, store.Search(new FileSearchQuery("quail")).Count);
        Assert.Equal(Path.Combine(dataPath, "quail-alpha.txt"), store.ResolveOpenPath(alpha).Path);

        var secondStore = new IndexStore(Path.Combine(rootPath, "m11-host-fixture-second.db"));
        var gamma = Id("0000000000000005");
        secondStore.BuildFromRecords(
            new VolumeDescriptor("m11-controlled-host-fixture-second", dataPath + Path.DirectorySeparatorChar, "NTFS", "M11 fixture"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, "", 16, 0, 2));
                sink(new NamespaceRecord(gamma, root, "quail-gamma.txt", 0, 0, 2));
            },
            acquireMetadata: record => record.Name == "quail-gamma.txt" ? Metadata(dataPath, record.Name) : new FileMetadata(null, null));

        Assert.Equal(4, MultiIndexSearch.Search([store, secondStore], new FileSearchQuery("quail")).Count);

        var missingStore = new IndexStore(Path.Combine(rootPath, "m11-host-fixture-missing.db"));
        var missing = Id("0000000000000006");
        missingStore.BuildFromRecords(
            new VolumeDescriptor("m11-controlled-host-fixture-missing", dataPath + Path.DirectorySeparatorChar, "NTFS", "M11 fixture"),
            sink =>
            {
                sink(new NamespaceRecord(root, root, "", 16, 0, 2));
                sink(new NamespaceRecord(missing, root, "quail-missing.txt", 0, 0, 2));
            });

        Assert.Single(missingStore.Search(new FileSearchQuery("quail")));
    }

    private static FileMetadata Metadata(string root, string name)
    {
        var info = new FileInfo(Path.Combine(root, name));
        return new FileMetadata(info.Length, info.LastWriteTimeUtc.ToFileTimeUtc());
    }

    private static NativeFileId Id(string value) => new(Convert.FromHexString(value));
}
