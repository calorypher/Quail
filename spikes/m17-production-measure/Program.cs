using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quail.FileSystem;

namespace Quail.M17.ProductionMeasure;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Main(string[] arguments)
    {
        try
        {
            if (arguments.Length == 0) throw new ArgumentException("A command is required.");
            var options = Options.Parse(arguments);
            return options.Command switch
            {
                "report" => WriteReport(options),
                "decompose" => WriteDecomposition(options),
                "search" => MeasureSearch(options),
                "snapshot" => WriteSnapshot(options),
                "compare" => CompareSnapshots(options),
                _ => throw new ArgumentException("Command must be report, decompose, search, snapshot, or compare.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {exception.Message}");
            return 1;
        }
    }

    private static int WriteReport(Options options)
    {
        var indexPath = Require(options, "index");
        var outputPath = Require(options, "output");
        using var connection = OpenReadOnly(indexPath);
        var databaseBytes = new FileInfo(indexPath).Length;
        var compactBytes = ReadDbstatBytes(connection, "short_query_%");
        var postingPayloadBytes = ScalarLong(connection, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_posting_chunks;");
        var rankMapPayloadBytes = ScalarLong(connection, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_chunks;");
        var rankOrderPayloadBytes = ScalarLong(connection, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_order_chunks;");
        var postingCount = ScalarLong(connection, "SELECT coalesce(sum(posting_count), 0) FROM short_query_posting_chunks;");
        var postingChunks = ScalarLong(connection, "SELECT count(*) FROM short_query_posting_chunks;");
        var records = ScalarLong(connection, "SELECT count(*) FROM namespace_entries;");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var searchRankMapLoad = MeasurePayloadLoad(connection, "SELECT payload FROM short_query_rank_chunks;");
        var maintenanceRankOrderLoad = MeasurePayloadLoad(connection, "SELECT payload FROM short_query_rank_order_chunks;");

        double? directBuildMilliseconds = null;
        long? directCompactBuildDatabaseBytes = null;
        if (options.Values.TryGetValue("work-copy", out var workCopy))
        {
            if (File.Exists(workCopy)) throw new ArgumentException("--work-copy must not already exist.");
            File.Copy(indexPath, workCopy);
            using (var writable = OpenReadWrite(workCopy))
            {
                ClearDerivedState(writable);
                Vacuum(writable);
                var build = Stopwatch.StartNew();
                ShortQueryIndex.Build(writable);
                build.Stop();
                directBuildMilliseconds = Math.Round(build.Elapsed.TotalMilliseconds, 3);
            }

            directCompactBuildDatabaseBytes = new FileInfo(workCopy).Length;
        }

        Write(outputPath, new
        {
            schemaVersion = 1,
            kind = "m17-production-compact-report",
            indexPath = Path.GetFullPath(indexPath),
            databaseBytes,
            baseDatabaseBytes = compactBytes is null ? (long?)null : databaseBytes - compactBytes.Value,
            compactDerivedBytes = compactBytes,
            compactGrowthPercent = compactBytes is null || databaseBytes == compactBytes.Value
                ? (double?)null
                : Math.Round(compactBytes.Value * 100d / (databaseBytes - compactBytes.Value), 3),
            records,
            postings = postingCount,
            postingChunks,
            bytesPerPosting = postingCount == 0 ? (double?)null : Math.Round((double)(compactBytes ?? postingPayloadBytes + rankMapPayloadBytes + rankOrderPayloadBytes) / postingCount, 3),
            postingPayloadBytes,
            rankMapPayloadBytes,
            rankOrderPayloadBytes,
            logicalCompactPayloadBytes = postingPayloadBytes + rankMapPayloadBytes + rankOrderPayloadBytes,
            searchRankMapLoadMilliseconds = searchRankMapLoad.ElapsedMilliseconds,
            searchRankMapLoadPayloadBytes = searchRankMapLoad.PayloadBytes,
            searchRankMapManagedMemoryDeltaBytes = searchRankMapLoad.ManagedMemoryDeltaBytes,
            maintenanceRankOrderLoadMilliseconds = maintenanceRankOrderLoad.ElapsedMilliseconds,
            maintenanceRankOrderLoadPayloadBytes = maintenanceRankOrderLoad.PayloadBytes,
            maintenanceRankOrderManagedMemoryDeltaBytes = maintenanceRankOrderLoad.ManagedMemoryDeltaBytes,
            directCompactBuildMilliseconds = directBuildMilliseconds,
            directCompactBuildDatabaseBytes,
            directCompactBuildCopy = workCopy is null ? null : Path.GetFullPath(workCopy),
            note = "searchRankMapLoad measures the rank map read by short-query Search. maintenanceRankOrderLoad separately measures order-maintenance state that Search does not load. dbstat-derived bytes include SQLite pages owned by short_query_* tables and indexes. The optional work copy is modified only to measure a deterministic compact-state rebuild from namespace_entries."
        });
        return 0;
    }

    private static int WriteDecomposition(Options options)
    {
        var indexPath = Require(options, "index");
        var outputPath = Require(options, "output");
        var baseCopy = Require(options, "base-copy");
        if (File.Exists(baseCopy)) throw new ArgumentException("--base-copy must not already exist.");

        using var source = OpenReadOnly(indexPath);
        var sourcePageState = ReadPageState(source);
        var postingPayloadBytes = ScalarLong(source, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_posting_chunks;");
        var rankMapPayloadBytes = ScalarLong(source, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_chunks;");
        var rankOrderPayloadBytes = ScalarLong(source, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_order_chunks;");
        var postingCount = ScalarLong(source, "SELECT coalesce(sum(posting_count), 0) FROM short_query_posting_chunks;");
        var records = ScalarLong(source, "SELECT count(*) FROM namespace_entries;");
        var databaseBytes = new FileInfo(indexPath).Length;
        var shortQueryFormat = ScalarString(source, "SELECT value FROM metadata WHERE key='short_query_format';");
        var labelSpacingImpact = shortQueryFormat == "compact-short-query-v2"
            ? ReadLabelSpacingImpact(source, postingCount)
            : null;

        File.Copy(indexPath, baseCopy);
        PageState afterDrop;
        long baseDatabaseBytes;
        using (var copy = OpenReadWrite(baseCopy))
        {
            DropDerivedSchema(copy);
            afterDrop = ReadPageState(copy);
            using var vacuum = copy.CreateCommand();
            vacuum.CommandText = "VACUUM;";
            vacuum.ExecuteNonQuery();
            baseDatabaseBytes = new FileInfo(baseCopy).Length;
        }

        Write(outputPath, new
        {
            schemaVersion = 1,
            kind = "m17-production-footprint-decomposition",
            indexPath = Path.GetFullPath(indexPath),
            baseCopy = Path.GetFullPath(baseCopy),
            databaseBytes,
            records,
            postings = postingCount,
            postingPayloadBytes,
            rankMapPayloadBytes,
            rankOrderPayloadBytes,
            logicalCompactPayloadBytes = postingPayloadBytes + rankMapPayloadBytes + rankOrderPayloadBytes,
            labelSpacingImpact,
            sourcePageState,
            afterDrop,
            reclaimedDerivedPageBytesBeforeVacuum = checked((afterDrop.FreeListPages - sourcePageState.FreeListPages) * sourcePageState.PageSize),
            baseDatabaseBytesAfterVacuum = baseDatabaseBytes,
            derivedAndStructuralBytesAboveVacuumedBase = databaseBytes - baseDatabaseBytes,
            note = "The label-spacing projection is exact for this frozen direct-build corpus: every stored label is validated as a multiple of the production 2^32 spacing, then its posting deltas are re-encoded at each listed spacing without changing chunk boundaries. The disposable base copy drops only short_query_* tables and indexes, then VACUUMs. derivedAndStructuralBytesAboveVacuumedBase includes the compact tables, their indexes, and any source-database free-page fragmentation removed by VACUUM. reclaimedDerivedPageBytesBeforeVacuum is the directly observable increase in SQLite free-list bytes after the drop."
        });
        return 0;
    }

    private static int MeasureSearch(Options options)
    {
        var indexPath = Require(options, "index");
        var outputPath = Require(options, "output");
        var queries = Require(options, "queries")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var repetitions = options.Values.TryGetValue("repetitions", out var repetitionsValue)
            ? int.Parse(repetitionsValue, System.Globalization.CultureInfo.InvariantCulture)
            : 1;
        if (repetitions < 1) throw new ArgumentException("--repetitions must be at least one.");
        if (queries.Length == 0 || queries.Any(query => query.Length is < 1 or > 2))
        {
            throw new ArgumentException("--queries must contain one- or two-character values.");
        }

        using var connection = OpenReadOnly(indexPath);
        var context = FileSearchRankingContext.ForCurrentMachine();
        var samples = new List<object>();
        foreach (var query in queries)
        {
            for (var iteration = 1; iteration <= repetitions; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                var results = ShortQueryIndex.Search(connection, query, 50, context);
                stopwatch.Stop();
                samples.Add(new
                {
                    queryLength = query.Length,
                    iteration,
                    resultCount = results.Count,
                    searchMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3)
                });
            }
        }

        Write(outputPath, new
        {
            schemaVersion = 1,
            kind = "m17-production-short-query-search",
            indexPath = Path.GetFullPath(indexPath),
            repetitions,
            samples,
            note = "This is the production ShortQueryIndex.Search path on the supplied SQLite copy. It includes rank-map loading, runtime location classification, posting decode, and result reconstruction, but excludes App/Core/UI scheduling and rendering."
        });
        return 0;
    }

    private static int WriteSnapshot(Options options)
    {
        var indexPath = Require(options, "index");
        using var connection = OpenReadOnly(indexPath);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT chunk_id, length(payload), payload FROM short_query_posting_chunks ORDER BY chunk_id;";
        using var reader = command.ExecuteReader();
        var chunks = new List<ChunkSnapshot>();
        while (reader.Read())
        {
            var payload = (byte[])reader[2];
            chunks.Add(new ChunkSnapshot(reader.GetInt64(0), reader.GetInt64(1), Convert.ToHexString(SHA256.HashData(payload))));
        }

        Write(Require(options, "output"), new SnapshotDocument(1, "m17-posting-chunk-snapshot", Path.GetFullPath(indexPath), chunks));
        return 0;
    }

    private static int CompareSnapshots(Options options)
    {
        var before = Read<SnapshotDocument>(Require(options, "before"));
        var after = Read<SnapshotDocument>(Require(options, "after"));
        if (before.SchemaVersion != 1 || after.SchemaVersion != 1) throw new InvalidOperationException("Unsupported snapshot version.");
        var beforeById = before.Chunks.ToDictionary(chunk => chunk.ChunkId);
        var afterById = after.Chunks.ToDictionary(chunk => chunk.ChunkId);
        var changed = after.Chunks.Where(chunk => !beforeById.TryGetValue(chunk.ChunkId, out var previous) || previous.PayloadHash != chunk.PayloadHash).ToArray();
        var removed = before.Chunks.Where(chunk => !afterById.ContainsKey(chunk.ChunkId)).ToArray();
        Write(Require(options, "output"), new
        {
            schemaVersion = 1,
            kind = "m17-posting-chunk-mutation-comparison",
            before = before.IndexPath,
            after = after.IndexPath,
            changedChunkCount = changed.Length,
            removedChunkCount = removed.Length,
            afterPayloadBytesForChangedChunks = changed.Sum(chunk => chunk.PayloadBytes),
            beforePayloadBytesForRemovedChunks = removed.Sum(chunk => chunk.PayloadBytes),
            maximumAfterPayloadBytes = changed.Length == 0 ? 0 : changed.Max(chunk => chunk.PayloadBytes),
            note = "Payload-byte figures are a focused lower bound for the posting-chunk mutation; SQLite page and transaction overhead are not included."
        });
        return 0;
    }

    private static void ClearDerivedState(SqliteConnection connection)
    {
        DropDerivedSchema(connection);
        ShortQueryIndex.CreateSchema(connection);
    }

    private static PayloadLoad MeasurePayloadLoad(SqliteConnection connection, string sql)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var payloads = new List<byte[]>();
        while (reader.Read()) payloads.Add((byte[])reader[0]);
        stopwatch.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);
        return new PayloadLoad(
            Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            payloads.Sum(payload => (long)payload.Length),
            memoryAfter - memoryBefore);
    }

    private static PageState ReadPageState(SqliteConnection connection) => new(
        ScalarLong(connection, "PRAGMA page_size;"),
        ScalarLong(connection, "PRAGMA page_count;"),
        ScalarLong(connection, "PRAGMA freelist_count;"));

    private static void DropDerivedSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            DROP INDEX IF EXISTS ix_short_query_posting_chunks_term;
            DROP INDEX IF EXISTS ix_short_query_rank_chunks_label;
            DROP INDEX IF EXISTS ix_short_query_rank_order_chunks_sort;
            DROP TABLE IF EXISTS short_query_posting_chunks;
            DROP TABLE IF EXISTS short_query_rank_chunks;
            DROP TABLE IF EXISTS short_query_rank_order_chunks;
            DELETE FROM metadata WHERE key IN ('short_query_format', 'namespace_generation', 'short_query_generation');
            """;
        command.ExecuteNonQuery();
    }

    private static void Vacuum(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "VACUUM;";
        command.ExecuteNonQuery();
    }

    private static LabelSpacingImpact ReadLabelSpacingImpact(SqliteConnection connection, long postingCount)
    {
        const long productionSpacing = 1L << 32;
        long[] spacings = [1, 1L << 12, 1L << 16, 1L << 20, productionSpacing];
        var payloadBytes = new long[spacings.Length];
        var varintWidths = new long[10];
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM short_query_posting_chunks ORDER BY chunk_id;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var payload = (byte[])reader[0];
            var offset = 0;
            long previousCurrentLabel = 0;
            var previousProjectedLabels = new long[spacings.Length];
            while (offset < payload.Length)
            {
                var start = offset;
                var delta = ReadVarint(payload, ref offset);
                var encodedWidth = offset - start;
                varintWidths[encodedWidth]++;
                if (delta > long.MaxValue || previousCurrentLabel > long.MaxValue - (long)delta)
                {
                    throw new InvalidOperationException("Short-query posting payload is invalid.");
                }

                previousCurrentLabel += (long)delta;
                if (previousCurrentLabel % productionSpacing != 0)
                {
                    throw new InvalidOperationException("Frozen corpus does not contain direct-build 2^32-spaced labels.");
                }

                var ordinal = previousCurrentLabel / productionSpacing;
                for (var index = 0; index < spacings.Length; index++)
                {
                    var projectedLabel = checked(ordinal * spacings[index]);
                    var projectedDelta = checked(projectedLabel - previousProjectedLabels[index]);
                    payloadBytes[index] += VarintWidth((ulong)projectedDelta);
                    previousProjectedLabels[index] = projectedLabel;
                }
            }
        }

        if (varintWidths.Sum() != postingCount) throw new InvalidOperationException("Short-query posting count does not match payloads.");
        return new LabelSpacingImpact(
            varintWidths.Select((count, width) => new VarintWidthCount(width, count)).Where(item => item.Count != 0).ToArray(),
            spacings.Select((spacing, index) => new SpacingProjection(
                spacing,
                payloadBytes[index],
                Math.Round((double)payloadBytes[index] / postingCount, 3))).ToArray());
    }

    private static int VarintWidth(ulong value)
    {
        var width = 1;
        while (value >= 0x80)
        {
            width++;
            value >>= 7;
        }

        return width;
    }

    private static ulong ReadVarint(byte[] payload, ref int offset)
    {
        ulong value = 0;
        var shift = 0;
        while (true)
        {
            if (offset >= payload.Length || shift > 63) throw new InvalidOperationException("Short-query posting payload is invalid.");
            var next = payload[offset++];
            value |= (ulong)(next & 0x7f) << shift;
            if ((next & 0x80) == 0) return value;
            shift += 7;
        }
    }

    private static long? ReadDbstatBytes(SqliteConnection connection, string namePattern)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT coalesce(sum(pgsize), 0) FROM dbstat WHERE name LIKE $pattern;";
            command.Parameters.AddWithValue("$pattern", namePattern);
            return Convert.ToInt64(command.ExecuteScalar());
        }
        catch (SqliteException)
        {
            return null;
        }
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string? ScalarString(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenReadWrite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("Measurement JSON is invalid.");
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    private static string Require(Options options, string name) => options.Values.TryGetValue(name, out var value) ? value : throw new ArgumentException($"--{name} is required.");

    private sealed record ChunkSnapshot(long ChunkId, long PayloadBytes, string PayloadHash);
    private sealed record SnapshotDocument(int SchemaVersion, string Kind, string IndexPath, List<ChunkSnapshot> Chunks);
    private sealed record PayloadLoad(double ElapsedMilliseconds, long PayloadBytes, long ManagedMemoryDeltaBytes);
    private sealed record PageState(long PageSize, long PageCount, long FreeListPages);
    private sealed record LabelSpacingImpact(IReadOnlyList<VarintWidthCount> CurrentVarintWidthCounts, IReadOnlyList<SpacingProjection> Projections);
    private sealed record VarintWidthCount(int Width, long Count);
    private sealed record SpacingProjection(long Spacing, long PostingPayloadBytes, double BytesPerPosting);

    private sealed class Options
    {
        public string Command { get; }
        public Dictionary<string, string> Values { get; }

        private Options(string command, Dictionary<string, string> values)
        {
            Command = command;
            Values = values;
        }

        public static Options Parse(string[] arguments)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < arguments.Length; index += 2)
            {
                if (!arguments[index].StartsWith("--", StringComparison.Ordinal) || index + 1 == arguments.Length)
                {
                    throw new ArgumentException("Options must be --name value pairs.");
                }

                if (!values.TryAdd(arguments[index][2..], arguments[index + 1])) throw new ArgumentException($"Duplicate option '{arguments[index]}'.");
            }

            return new Options(arguments[0].ToLowerInvariant(), values);
        }
    }
}
