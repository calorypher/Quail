using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quail.FileSystem;

namespace Quail.M17.ShortQuerySpike;

internal static class Program
{
    private const int DefaultLimit = 50;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static int Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            return options.Command switch
            {
                "build" => Build(options),
                "measure" => Measure(options),
                "verify" => Verify(options),
                "self-test" => SelfTest(options),
                _ => throw new ArgumentException("Command must be build, measure, or verify.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {exception.Message}");
            return 1;
        }
    }

    private static int Build(Options options)
    {
        EnsureAbsent(options.OutputPath);
        var sampler = new WorkingSetSampler();
        sampler.Start();
        var stopwatch = Stopwatch.StartNew();
        long records = 0;
        long postings = 0;
        try
        {
            using var output = OpenReadWrite(options.OutputPath);
            CreateSchema(output);
            using var transaction = output.BeginTransaction();
            using var insert = output.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO short_terms(term,match_class,entry_rowid,name) VALUES($term,$class,$rowid,$name);";
            var term = insert.Parameters.Add("$term", SqliteType.Text);
            var matchClass = insert.Parameters.Add("$class", SqliteType.Integer);
            var rowId = insert.Parameters.Add("$rowid", SqliteType.Integer);
            var name = insert.Parameters.Add("$name", SqliteType.Text);

            using var input = OpenReadOnly(options.SourcePath);
            using var read = input.CreateCommand();
            read.CommandText = "SELECT rowid,name FROM namespace_entries ORDER BY rowid;";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var entryRowId = reader.GetInt64(0);
                var entryName = reader.GetString(1);
                foreach (var candidate in ShortTerms.ForName(entryName))
                {
                    term.Value = candidate.Term;
                    matchClass.Value = (int)candidate.MatchClass;
                    rowId.Value = entryRowId;
                    name.Value = entryName;
                    insert.ExecuteNonQuery();
                    postings++;
                }

                records++;
            }

            transaction.Commit();
            using var optimize = output.CreateCommand();
            optimize.CommandText = "ANALYZE; VACUUM;";
            optimize.ExecuteNonQuery();
        }
        finally
        {
            stopwatch.Stop();
            sampler.Stop();
        }

        Write(options.EvidencePath, new
        {
            schemaVersion = 1,
            kind = "sqlite-dense-postings-build",
            sourceBytes = new FileInfo(options.SourcePath).Length,
            auxiliaryBytes = new FileInfo(options.OutputPath).Length,
            records,
            postings,
            elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            peakWorkingSetBytes = sampler.PeakWorkingSetBytes
        });
        return 0;
    }

    private static int Measure(Options options)
    {
        var queries = options.Queries;
        var samples = new List<object>();
        using var source = OpenReadOnly(options.SourcePath);
        using var auxiliary = OpenReadOnly(options.OutputPath);
        foreach (var querySpec in queries)
        {
            var query = ResolveQuery(auxiliary, querySpec);
            for (var iteration = 1; iteration <= options.Repetitions; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                var candidates = ReadCandidates(source, auxiliary, query, options.Limit);
                var results = Rank(source, candidates, query, options.Limit);
                stopwatch.Stop();
                samples.Add(new
                {
                    queryLength = query.Length,
                    queryKind = options.QueryKinds.TryGetValue(querySpec, out var kind) ? kind : "unspecified",
                    iteration,
                    elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    candidateCount = candidates.Count,
                    resultCount = results.Count,
                    resultFingerprint = Fingerprint(results)
                });
            }
        }

        Write(options.EvidencePath, new
        {
            schemaVersion = 1,
            kind = "sqlite-dense-postings-search",
            auxiliaryBytes = new FileInfo(options.OutputPath).Length,
            limit = options.Limit,
            samples
        });
        return 0;
    }

    private static int Verify(Options options)
    {
        using var source = OpenReadOnly(options.SourcePath);
        using var auxiliary = OpenReadOnly(options.OutputPath);
        var checks = new List<object>();
        foreach (var query in options.Queries)
        {
            var expected = ScalarLong(source, "SELECT count(*) FROM namespace_entries WHERE instr(lower(name), lower($query)) > 0;", query);
            var actual = ScalarLong(auxiliary, "SELECT count(DISTINCT entry_rowid) FROM short_terms WHERE term = $query;", query);
            var exactCandidates = ScalarLong(auxiliary, "SELECT count(*) FROM short_terms WHERE term = $query AND match_class = $class;", query, (int)ShortMatchClass.Exact);
            if (expected != actual)
            {
                throw new InvalidOperationException($"Recall verification failed for query length {query.Length}.");
            }

            checks.Add(new
            {
                queryLength = query.Length,
                matchingEntries = expected,
                exactCandidates,
                fullRecall = true,
                rankingEquivalent = false,
                rankingEquivalenceReason = "The dense table omits static location and path rank keys; a bounded per-text-class lookup is not a correct final ranking path."
            });
        }

        Write(options.EvidencePath, new { schemaVersion = 1, kind = "sqlite-dense-postings-verification", checks });
        return 0;
    }

    private static int SelfTest(Options options)
    {
        var checks = new List<object>();
        foreach (var (query, names) in new[]
        {
            ("a", new[] { "ba", "ca", "da", "ea", "a" }),
            ("ks", new[] { "bks", "cks", "dks", "eks", "ks" })
        })
        {
            var candidates = names
                .Select((name, rowId) => new
                {
                    Name = name,
                    MatchClass = ShortTerms.ForName(name)
                        .Single(term => string.Equals(term.Term, query, StringComparison.OrdinalIgnoreCase))
                        .MatchClass,
                    RowId = rowId
                })
                .OrderBy(candidate => candidate.MatchClass)
                .ThenBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.RowId)
                .ToArray();
            if (candidates[0].Name != query || candidates.Length != names.Length)
            {
                throw new InvalidOperationException($"Late exact-match guard failed for query length {query.Length}.");
            }

            checks.Add(new { queryLength = query.Length, fullCandidateCount = candidates.Length, laterExactMatchFirst = true });
        }

        Write(options.EvidencePath, new { schemaVersion = 1, kind = "late-exact-match-guards", checks });
        return 0;
    }

    private static List<Candidate> ReadCandidates(SqliteConnection source, SqliteConnection auxiliary, string query, int limit)
    {
        var candidates = new Dictionary<long, Candidate>();
        foreach (var matchClass in Enum.GetValues<ShortMatchClass>())
        {
            using var command = auxiliary.CreateCommand();
            command.CommandText = "SELECT entry_rowid,name FROM short_terms WHERE term = $query AND match_class = $class ORDER BY name COLLATE NOCASE, name COLLATE BINARY, entry_rowid LIMIT $limit;";
            command.Parameters.AddWithValue("$query", query);
            command.Parameters.AddWithValue("$class", (int)matchClass);
            command.Parameters.AddWithValue("$limit", limit);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var rowId = reader.GetInt64(0);
                candidates.TryAdd(rowId, new Candidate(rowId, reader.GetString(1)));
            }
        }

        return candidates.Values.ToList();
    }

    private static IReadOnlyList<FileSearchResult> Rank(SqliteConnection source, IReadOnlyList<Candidate> candidates, string query, int limit)
    {
        var results = new List<FileSearchResult>(candidates.Count);
        var mountPoint = ReadMountPoint(source);
        foreach (var candidate in candidates)
        {
            using var command = source.CreateCommand();
            command.CommandText = "SELECT file_id,name,attributes,logical_size,last_write_time_utc FROM namespace_entries WHERE rowid = $rowid;";
            command.Parameters.AddWithValue("$rowid", candidate.RowId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Auxiliary posting references a missing entry.");
            var fileId = new NativeFileId((byte[])reader[0]);
            var name = reader.GetString(1);
            var attributes = checked((uint)reader.GetInt64(2));
            results.Add(new FileSearchResult(
                fileId,
                name,
                ReconstructPath(source, fileId, mountPoint),
                (attributes & 0x10) != 0,
                Path.GetExtension(name).TrimStart('.'),
                reader.IsDBNull(3) ? null : reader.GetInt64(3),
                reader.IsDBNull(4) ? null : reader.GetInt64(4),
                attributes));
        }

        var context = FileSearchRankingContext.ForCurrentMachine();
        return results.OrderBy(result => result, Comparer<FileSearchResult>.Create((left, right) => FileSearchRanking.Compare(left, right, query, context))).Take(limit).ToArray();
    }

    private static string? ReconstructPath(SqliteConnection connection, NativeFileId fileId, string mountPoint)
    {
        var parts = new List<string>();
        var current = fileId.Bytes.ToArray();
        for (var depth = 0; depth < 256; depth++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT parent_file_id,name FROM namespace_entries WHERE file_id = $fileId;";
            command.Parameters.AddWithValue("$fileId", current);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return null;
            var parent = (byte[])reader[0];
            var name = reader.GetString(1);
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (parent.AsSpan().SequenceEqual(current)) break;
            current = parent;
        }

        parts.Reverse();
        return Path.Combine(mountPoint, Path.Combine(parts.ToArray()));
    }

    private static string ReadMountPoint(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = 'mount_point';";
        return (string?)command.ExecuteScalar() ?? throw new InvalidOperationException("Source index has no mount point.");
    }

    private static long ScalarLong(SqliteConnection connection, string sql, string query, int? matchClass = null)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$query", query);
        if (matchClass is not null) command.Parameters.AddWithValue("$class", matchClass.Value);
        return (long)command.ExecuteScalar()!;
    }

    private static string ResolveQuery(SqliteConnection connection, string querySpec)
    {
        if (!querySpec.StartsWith('@')) return querySpec;
        var length = querySpec.Contains("two", StringComparison.Ordinal) ? 2 : 1;
        var requiresExact = querySpec.EndsWith("exact", StringComparison.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = requiresExact
            ? "SELECT term FROM short_terms WHERE length(term) = $length AND match_class = 0 GROUP BY term ORDER BY count(*) DESC, term LIMIT 1;"
            : "SELECT term FROM short_terms WHERE length(term) = $length GROUP BY term HAVING sum(CASE WHEN match_class = 0 THEN 1 ELSE 0 END) = 0 ORDER BY count(*) DESC, term LIMIT 1;";
        command.Parameters.AddWithValue("$length", length);
        return (string?)command.ExecuteScalar() ?? throw new InvalidOperationException($"No {length}-character query satisfies '{querySpec}'.");
    }

    private static string Fingerprint(IEnumerable<FileSearchResult> results)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('|', results.Select(result => result.FileId.ToString())));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE short_terms(term TEXT NOT NULL COLLATE NOCASE,match_class INTEGER NOT NULL,entry_rowid INTEGER NOT NULL,name TEXT NOT NULL,PRIMARY KEY(term,match_class,name COLLATE NOCASE,name COLLATE BINARY,entry_rowid)) WITHOUT ROWID;";
        command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static SqliteConnection OpenReadWrite(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static void EnsureAbsent(string path)
    {
        if (File.Exists(path)) throw new InvalidOperationException("Output already exists; use a new path.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    }

    private static void Write(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptions));
    }

    private sealed class WorkingSetSampler
    {
        private readonly CancellationTokenSource _stop = new();
        private Task? _task;
        public long PeakWorkingSetBytes { get; private set; }
        public void Start() => _task = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                PeakWorkingSetBytes = Math.Max(PeakWorkingSetBytes, Process.GetCurrentProcess().WorkingSet64);
                await Task.Delay(100, _stop.Token).ConfigureAwait(false);
            }
        });
        public void Stop()
        {
            _stop.Cancel();
            try { _task?.Wait(); } catch (AggregateException) { }
        }
    }

    private sealed record Candidate(long RowId, string Name);

    private enum ShortMatchClass { Exact, Prefix, TokenPrefix, Substring }

    private sealed record ShortTerm(string Term, ShortMatchClass MatchClass);

    private static class ShortTerms
    {
        private const string TokenSeparators = " -_.()[]{};, +&!@#$^=~`\"'";
        public static IEnumerable<ShortTerm> ForName(string name)
        {
            var terms = new Dictionary<string, ShortMatchClass>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < name.Length; index++)
            {
                for (var length = 1; length <= 2 && index + length <= name.Length; length++)
                {
                    var term = name.Substring(index, length);
                    var matchClass = Classify(name, term);
                    if (!terms.TryGetValue(term, out var current) || matchClass < current) terms[term] = matchClass;
                }
            }

            return terms.Select(pair => new ShortTerm(pair.Key, pair.Value));
        }

        private static ShortMatchClass Classify(string name, string term)
        {
            if (string.Equals(name, term, StringComparison.OrdinalIgnoreCase)) return ShortMatchClass.Exact;
            if (name.StartsWith(term, StringComparison.OrdinalIgnoreCase)) return ShortMatchClass.Prefix;
            for (var index = 1; index <= name.Length - term.Length; index++)
            {
                if (TokenSeparators.IndexOf(name[index - 1]) >= 0 && name.AsSpan(index).StartsWith(term, StringComparison.OrdinalIgnoreCase)) return ShortMatchClass.TokenPrefix;
            }

            return ShortMatchClass.Substring;
        }
    }

    private sealed record Options(string Command, string SourcePath, string OutputPath, string EvidencePath, int Limit, int Repetitions, IReadOnlyList<string> Queries, IReadOnlyDictionary<string, string> QueryKinds)
    {
        public static Options Parse(string[] args)
        {
            if (args.Length == 0) throw new ArgumentException("A command is required.");
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Options must use --name value pairs.");
                values.Add(args[index][2..], args[index + 1]);
            }

            string Required(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? Path.GetFullPath(value) : throw new ArgumentException($"--{name} is required.");
            var queryPairs = values.TryGetValue("queries", out var queryValue)
                ? queryValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(pair => pair.Split(':', 2)).ToArray()
                : Array.Empty<string[]>();
            var queries = queryPairs.Select(pair => pair[0]).ToArray();
            if (args[0] is "measure" or "verify" && queries.Length == 0) throw new ArgumentException("--queries is required for measure and verify.");
            return new Options(
                args[0],
                Required("source"),
                Required("output"),
                Required("evidence"),
                values.TryGetValue("limit", out var limit) ? int.Parse(limit) : DefaultLimit,
                values.TryGetValue("repetitions", out var repetitions) ? int.Parse(repetitions) : 1,
                queries,
                queryPairs.Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal));
        }
    }
}
