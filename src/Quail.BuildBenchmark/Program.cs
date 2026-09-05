using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Quail.FileSystem;

var options = BenchmarkOptions.Parse(args);
Directory.CreateDirectory(options.OutputDirectory);

var store = new IndexStore(options.DatabasePath, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
var startedAt = DateTimeOffset.UtcNow;
BuildMetrics metrics;
VolumeDescriptor volume;

if (options.Fixture)
{
    volume = new VolumeDescriptor("m17-5-fixture", "X:\\", "NTFS", "fixture");
    Action<Action<NamespaceRecord>> fixtureRecords = sink =>
    {
        var root = Id(1);
        var folder = Id(2);
        sink(new NamespaceRecord(root, root, string.Empty, 0x10, 0, 2));
        sink(new NamespaceRecord(folder, root, "fixture", 0x10, 0, 2));
        for (var index = 3; index < 19; index++)
        {
            sink(new NamespaceRecord(Id((ulong)index), folder, $"fixture-{index:D2}.txt", 0, index, 2));
        }
    };
    metrics = options.DiagnosticNoFts
        ? store.BuildFromRecordsWithoutFtsForTesting(volume, fixtureRecords, _ => new FileMetadata(64, 1))
        : store.BuildFromRecords(volume, fixtureRecords, acquireMetadata: _ => new FileMetadata(64, 1));
}
else
{
    volume = NtfsVolume.Validate(options.MountPoint);
    metrics = options.DiagnosticNoFts
        ? store.BuildWithoutFtsForBenchmark(volume.MountPoint)
        : store.Build(volume.MountPoint);
}

var status = store.GetStatus();
if (!options.DiagnosticNoFts)
{
    store.EnsureSearchReady();
}
var databaseBytes = new FileInfo(options.DatabasePath).Length;
var phases = metrics.Phases ?? throw new InvalidOperationException("Build did not provide phase metrics.");
var result = new
{
    schemaVersion = 1,
    capturedAtUtc = startedAt.ToUniversalTime().ToString("O"),
    gitHead = options.GitHead,
    sourceDirty = options.SourceDirty,
    dotnetVersion = options.DotnetVersion,
    osVersion = Environment.OSVersion.VersionString,
    runNumber = options.RunNumber,
    runKind = options.RunKind,
    fixture = options.Fixture,
    diagnosticMode = options.DiagnosticNoFts ? "no-fts" : null,
    volume = new
    {
        mountPoint = volume.MountPoint,
        fileSystem = volume.FileSystem,
        stableIdentitySha256 = Sha256(volume.StableIdentity)
    },
    recordCount = status.RecordCount,
    enumeratedRecordCount = metrics.RecordCount,
    databaseBytes,
    bytesPerObject = status.RecordCount == 0 ? null : (double?)databaseBytes / status.RecordCount,
    elapsedMilliseconds = metrics.Elapsed.TotalMilliseconds,
    cpuTimeMilliseconds = metrics.CpuTime.TotalMilliseconds,
    cpuWallRatio = metrics.Elapsed == TimeSpan.Zero ? null : (double?)(metrics.CpuTime.TotalMilliseconds / metrics.Elapsed.TotalMilliseconds),
    peakWorkingSetBytes = metrics.PeakWorkingSetBytes,
    metadata = metrics.Metadata is null ? null : new
    {
        attempts = metrics.Metadata.Attempts,
        succeeded = metrics.Metadata.Succeeded,
        failed = metrics.Metadata.Failed,
        failureCodes = metrics.Metadata.FailureCodes
    },
    phaseMilliseconds = new
    {
        setupSchema = phases.SetupSchema.TotalMilliseconds,
        mftEnumerationReadParse = phases.MftEnumerationReadParse.TotalMilliseconds,
        metadataAcquisition = phases.MetadataAcquisition.TotalMilliseconds,
        namespaceAndFtsWrites = phases.NamespaceAndFtsWrites.TotalMilliseconds,
        bulkTransactionCommits = phases.BulkTransactionCommits.TotalMilliseconds,
        journalHandoff = phases.JournalHandoff.TotalMilliseconds,
        namespaceNormalization = phases.NamespaceNormalization.TotalMilliseconds,
        bulkFtsBuild = phases.BulkFtsBuild.TotalMilliseconds,
        shortQueryBuild = phases.ShortQueryBuild.TotalMilliseconds,
        checkpointFinalization = phases.CheckpointFinalization.TotalMilliseconds,
        stagingPromotion = phases.StagingPromotion.TotalMilliseconds,
        residual = phases.Residual.TotalMilliseconds
    },
    storageBreakdown = new
    {
        ftsBytes = (long?)null,
        baseBytes = (long?)null,
        derivedBytes = (long?)null,
        shortQueryBytes = (long?)null
    },
    integrity = new
    {
        status = status.State.ToString(),
        searchReady = options.DiagnosticNoFts ? null : (bool?)true
    }
};

var fileName = $"{options.RunKind}-{options.RunNumber:D2}.json";
var outputPath = Path.Combine(options.OutputDirectory, fileName);
var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
File.WriteAllText(outputPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
Console.WriteLine(outputPath);

static NativeFileId Id(ulong value) => new(BitConverter.GetBytes(value));

static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

sealed record BenchmarkOptions(
    string DatabasePath,
    string OutputDirectory,
    string MountPoint,
    int RunNumber,
    string RunKind,
    string GitHead,
    bool SourceDirty,
    string DotnetVersion,
    bool Fixture,
    bool DiagnosticNoFts)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fixture = false;
        var diagnosticNoFts = false;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--fixture")
            {
                fixture = true;
                continue;
            }

            if (args[index] == "--diagnostic-no-fts")
            {
                diagnosticNoFts = true;
                continue;
            }

            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException("Expected named benchmark arguments.");
            }

            values.Add(args[index], args[++index]);
        }

        return new BenchmarkOptions(
            Required(values, "--database-path"),
            Required(values, "--output-directory"),
            Required(values, "--mount-point"),
            int.Parse(Required(values, "--run-number"), System.Globalization.CultureInfo.InvariantCulture),
            Required(values, "--run-kind"),
            Required(values, "--git-head"),
            bool.Parse(Required(values, "--source-dirty")),
            Required(values, "--dotnet-version"),
            fixture,
            diagnosticNoFts);
    }

    private static string Required(IReadOnlyDictionary<string, string> values, string name) =>
        values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing required argument {name}.");
}
