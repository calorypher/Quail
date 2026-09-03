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
                "snapshot" => WriteSnapshot(options),
                "compare" => CompareSnapshots(options),
                _ => throw new ArgumentException("Command must be report, snapshot, or compare.")
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
        var rankPayloadBytes = ScalarLong(connection, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_chunks;") +
            ScalarLong(connection, "SELECT coalesce(sum(length(payload)), 0) FROM short_query_rank_order_chunks;");
        var postingCount = ScalarLong(connection, "SELECT coalesce(sum(posting_count), 0) FROM short_query_posting_chunks;");
        var postingChunks = ScalarLong(connection, "SELECT count(*) FROM short_query_posting_chunks;");
        var records = ScalarLong(connection, "SELECT count(*) FROM namespace_entries;");

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var load = Stopwatch.StartNew();
        var rankPayloads = ReadRankPayloads(connection);
        load.Stop();
        var memoryAfter = GC.GetTotalMemory(forceFullCollection: false);

        double? directBuildMilliseconds = null;
        if (options.Values.TryGetValue("work-copy", out var workCopy))
        {
            if (File.Exists(workCopy)) throw new ArgumentException("--work-copy must not already exist.");
            File.Copy(indexPath, workCopy);
            var build = Stopwatch.StartNew();
            using (var writable = OpenReadWrite(workCopy))
            {
                ClearDerivedState(writable);
                ShortQueryIndex.Build(writable);
            }

            build.Stop();
            directBuildMilliseconds = Math.Round(build.Elapsed.TotalMilliseconds, 3);
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
            bytesPerPosting = postingCount == 0 ? (double?)null : Math.Round((double)(compactBytes ?? postingPayloadBytes + rankPayloadBytes) / postingCount, 3),
            postingPayloadBytes,
            rankPayloadBytes,
            rankLoadMilliseconds = Math.Round(load.Elapsed.TotalMilliseconds, 3),
            rankLoadPayloadBytes = rankPayloads.Sum(payload => (long)payload.Length),
            managedMemoryDeltaBytes = memoryAfter - memoryBefore,
            directCompactBuildMilliseconds = directBuildMilliseconds,
            directCompactBuildCopy = workCopy is null ? null : Path.GetFullPath(workCopy),
            note = "dbstat-derived bytes include SQLite pages owned by short_query_* tables and indexes. The optional work copy is modified only to measure a deterministic compact-state rebuild from namespace_entries."
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
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM short_query_posting_chunks; DELETE FROM short_query_rank_chunks; DELETE FROM short_query_rank_order_chunks; DELETE FROM metadata WHERE key IN ('short_query_format', 'namespace_generation', 'short_query_generation');";
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    private static List<byte[]> ReadRankPayloads(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM short_query_rank_chunks UNION ALL SELECT payload FROM short_query_rank_order_chunks;";
        using var reader = command.ExecuteReader();
        var payloads = new List<byte[]>();
        while (reader.Read()) payloads.Add((byte[])reader[0]);
        return payloads;
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
