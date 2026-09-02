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
                "distribution" => Distribution(options),
                "compact-build" => CompactBuild(options),
                "compact-measure" => CompactMeasure(options),
                "compact-verify" => CompactVerify(options),
                "compact-mutation" => CompactMutation(options),
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

            var compactTop = MergeTopOrdinals(
                new CompactRanks([0, 1, 2, 3, 4], [0, 0, 0, 0, 0], 0),
                new[] { ((int)ShortMatchClass.Substring, EncodeDeltas([0, 1, 2, 3])), ((int)ShortMatchClass.Exact, EncodeDeltas([4])) },
                1);
            if (compactTop.Count != 1 || compactTop[0] != 4)
            {
                throw new InvalidOperationException($"Compact late-exact merge guard failed for query length {query.Length}.");
            }

            var locationBeforeText = MergeTopOrdinals(
                new CompactRanks([0, 1], [0, 1], 0),
                new[] { ((int)ShortMatchClass.Substring, EncodeDeltas([0])), ((int)ShortMatchClass.Exact, EncodeDeltas([1])) },
                1);
            if (locationBeforeText.Count != 1 || locationBeforeText[0] != 0)
            {
                throw new InvalidOperationException($"Compact location ordering guard failed for query length {query.Length}.");
            }

            var textBeforeStaticRank = MergeTopOrdinals(
                new CompactRanks([0, 1], [0, 0], 0),
                new[] { ((int)ShortMatchClass.Substring, EncodeDeltas([0])), ((int)ShortMatchClass.Prefix, EncodeDeltas([1])) },
                1);
            if (textBeforeStaticRank.Count != 1 || textBeforeStaticRank[0] != 1)
            {
                throw new InvalidOperationException($"Compact text-class ordering guard failed for query length {query.Length}.");
            }

            checks.Add(new
            {
                queryLength = query.Length,
                fullCandidateCount = candidates.Length,
                laterExactMatchFirst = true,
                compactMergeLaterExactFirst = true,
                compactMergeLocationBeforeText = true,
                compactMergeTextBeforeStaticRank = true
            });
        }

        Write(options.EvidencePath, new { schemaVersion = 1, kind = "late-exact-match-guards", checks });
        return 0;
    }

    private static int Distribution(Options options)
    {
        var densePath = options.DensePath ?? options.OutputPath;
        using var dense = OpenReadOnly(densePath);
        var byLength = new List<object>();
        foreach (var length in new[] { 1, 2 })
        {
            var lengths = new List<long>();
            using var command = dense.CreateCommand();
            command.CommandText = "SELECT count(*) FROM short_terms WHERE length(term) = $length GROUP BY term;";
            command.Parameters.AddWithValue("$length", length);
            using var reader = command.ExecuteReader();
            while (reader.Read()) lengths.Add(reader.GetInt64(0));
            lengths.Sort();
            byLength.Add(new
            {
                termLength = length,
                distinctTerms = lengths.Count,
                postings = lengths.Sum(),
                p50 = Percentile(lengths, 0.50),
                p90 = Percentile(lengths, 0.90),
                p99 = Percentile(lengths, 0.99),
                maximum = lengths.Count == 0 ? 0 : lengths[^1],
                topOneSharePercent = Share(lengths, 1),
                topTenSharePercent = Share(lengths, 10),
                topHundredSharePercent = Share(lengths, 100)
            });
        }

        Write(options.EvidencePath, new { schemaVersion = 1, kind = "short-posting-distribution", byLength });
        return 0;
    }

    private static int CompactBuild(Options options)
    {
        var densePath = options.DensePath ?? throw new ArgumentException("compact-build requires --dense <dense-postings.db>.");
        EnsureAbsent(options.OutputPath);
        var sampler = new WorkingSetSampler();
        sampler.Start();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var source = OpenReadOnly(options.SourcePath);
            var ranks = BuildStaticRanks(source);
            using var dense = OpenReadOnly(densePath);
            using var compact = OpenReadWrite(options.OutputPath);
            CreateCompactSchema(compact);
            using var transaction = compact.BeginTransaction();
            WriteCompactRankMap(compact, transaction, ranks);
            using var insert = compact.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO compact_terms(term,match_class,payload,posting_count) VALUES($term,$class,$payload,$count);";
            var term = insert.Parameters.Add("$term", SqliteType.Text);
            var matchClass = insert.Parameters.Add("$class", SqliteType.Integer);
            var payload = insert.Parameters.Add("$payload", SqliteType.Blob);
            var count = insert.Parameters.Add("$count", SqliteType.Integer);

            using var read = dense.CreateCommand();
            read.CommandText = "SELECT term,match_class,entry_rowid FROM short_terms ORDER BY term COLLATE NOCASE,match_class;";
            using var reader = read.ExecuteReader();
            string? currentTerm = null;
            var currentClass = -1;
            var ordinals = new List<int>();
            long totalPostings = 0;
            long lists = 0;
            void Flush()
            {
                if (currentTerm is null) return;
                ordinals.Sort();
                term.Value = currentTerm;
                matchClass.Value = currentClass;
                payload.Value = EncodeDeltas(ordinals);
                count.Value = ordinals.Count;
                insert.ExecuteNonQuery();
                totalPostings += ordinals.Count;
                lists++;
                ordinals.Clear();
            }

            while (reader.Read())
            {
                var nextTerm = reader.GetString(0);
                var nextClass = reader.GetInt32(1);
                if (currentTerm is not null && (!string.Equals(currentTerm, nextTerm, StringComparison.OrdinalIgnoreCase) || currentClass != nextClass))
                {
                    Flush();
                }

                currentTerm = nextTerm;
                currentClass = nextClass;
                var rowId = checked((int)reader.GetInt64(2));
                ordinals.Add(checked(ranks.OrdinalByRowId[rowId] * options.RankLabelSpacing));
            }

            Flush();
            SetCompactMeta(compact, transaction, "source_record_count", ranks.RowIdByOrdinal.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetCompactMeta(compact, transaction, "rank_label_spacing", options.RankLabelSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture));
            transaction.Commit();
            stopwatch.Stop();
            sampler.Stop();
            var compactBytes = new FileInfo(options.OutputPath).Length;
            Write(options.EvidencePath, new
            {
                schemaVersion = 1,
                kind = "compact-delta-varint-postings-build",
                sourceBytes = new FileInfo(options.SourcePath).Length,
                denseInputBytes = new FileInfo(densePath).Length,
                compactBytes,
                records = ranks.RowIdByOrdinal.Length,
                postings = totalPostings,
                postingLists = lists,
                bytesPerPosting = Math.Round((double)compactBytes / totalPostings, 3),
                bytesPerIndexedEntry = Math.Round((double)compactBytes / ranks.RowIdByOrdinal.Length, 3),
                elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                peakWorkingSetBytes = sampler.PeakWorkingSetBytes
            });
        }
        finally
        {
            if (stopwatch.IsRunning) stopwatch.Stop();
            sampler.Stop();
        }

        return 0;
    }

    private static int CompactMeasure(Options options)
    {
        var samples = new List<object>();
        using var source = OpenReadOnly(options.SourcePath);
        using var compact = OpenReadOnly(options.OutputPath);
        var load = Stopwatch.StartNew();
        var ranks = ReadCompactRankMap(compact);
        load.Stop();
        foreach (var querySpec in options.Queries)
        {
            var query = ResolveCompactQuery(compact, querySpec);
            for (var iteration = 1; iteration <= options.Repetitions; iteration++)
            {
                var stopwatch = Stopwatch.StartNew();
                var rowIds = ReadCompactTopRowIds(compact, ranks, query, options.Limit);
                var results = Rank(source, rowIds.Select(rowId => new Candidate(rowId, string.Empty)).ToArray(), query, options.Limit);
                stopwatch.Stop();
                samples.Add(new
                {
                    queryLength = query.Length,
                    queryKind = options.QueryKinds.TryGetValue(querySpec, out var kind) ? kind : "unspecified",
                    iteration,
                    elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
                    resultCount = results.Count,
                    resultFingerprint = Fingerprint(results)
                });
            }
        }

        Write(options.EvidencePath, new
        {
            schemaVersion = 1,
            kind = "compact-delta-varint-search",
            compactBytes = new FileInfo(options.OutputPath).Length,
            openAndRankMapLoadMilliseconds = Math.Round(load.Elapsed.TotalMilliseconds, 3),
            rankMapBytes = ranks.SerializedBytes,
            limit = options.Limit,
            samples
        });
        return 0;
    }

    private static int CompactVerify(Options options)
    {
        using var source = OpenReadOnly(options.SourcePath);
        using var compact = OpenReadOnly(options.OutputPath);
        var ranks = ReadCompactRankMap(compact);
        var checks = new List<object>();
        foreach (var querySpec in options.Queries)
        {
            var query = ResolveCompactQuery(compact, querySpec);
            var expected = ScalarLong(source, "SELECT count(*) FROM namespace_entries WHERE instr(lower(name), lower($query)) > 0;", query);
            var actual = CompactPostingCount(compact, query);
            var rowIds = ReadCompactTopRowIds(compact, ranks, query, options.Limit);
            var reranked = Rank(source, rowIds.Select(rowId => new Candidate(rowId, string.Empty)).ToArray(), query, options.Limit)
                .Select(result => result.FileId)
                .ToArray();
            var direct = rowIds.Select(rowId => ReadFileId(source, rowId)).ToArray();
            if (expected != actual || !direct.SequenceEqual(reranked))
            {
                throw new InvalidOperationException($"Compact recall or ranking verification failed for query length {query.Length}.");
            }

            checks.Add(new { queryLength = query.Length, matchingEntries = expected, fullRecall = true, rankingEquivalent = true, resultFingerprint = FingerprintIds(direct) });
        }

        Write(options.EvidencePath, new { schemaVersion = 1, kind = "compact-delta-varint-verification", checks });
        return 0;
    }

    private static int CompactMutation(Options options)
    {
        using var source = OpenReadOnly(options.SourcePath);
        using var compact = OpenReadOnly(options.OutputPath);
        var byteSamples = new List<long>();
        var listSamples = new List<long>();
        using var entries = source.CreateCommand();
        entries.CommandText = "SELECT rowid,name FROM namespace_entries WHERE name <> '' AND rowid % 8191 = 0 ORDER BY rowid LIMIT 100;";
        using var reader = entries.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(1);
            long bytes = 0;
            long lists = 0;
            foreach (var term in ShortTerms.ForName(name))
            {
                using var lookup = compact.CreateCommand();
                lookup.CommandText = "SELECT length(payload) FROM compact_terms WHERE term = $term AND match_class = $class;";
                lookup.Parameters.AddWithValue("$term", term.Term);
                lookup.Parameters.AddWithValue("$class", (int)term.MatchClass);
                if (lookup.ExecuteScalar() is long payloadBytes)
                {
                    bytes += payloadBytes;
                    lists++;
                }
            }

            byteSamples.Add(bytes);
            listSamples.Add(lists);
        }

        byteSamples.Sort();
        listSamples.Sort();
        Write(options.EvidencePath, new
        {
            schemaVersion = 1,
            kind = "compact-whole-blob-delete-write-amplification",
            samples = byteSamples.Count,
            affectedPostingListsP50 = Percentile(listSamples, 0.50),
            affectedPostingListsP95 = Percentile(listSamples, 0.95),
            payloadBytesP50 = Percentile(byteSamples, 0.50),
            payloadBytesP95 = Percentile(byteSamples, 0.95),
            payloadBytesMaximum = byteSamples.Count == 0 ? 0 : byteSamples[^1],
            note = "Measured payload bytes are a lower bound for a delete or rename that rewrites whole per-term BLOB lists; SQLite page and transaction overhead are excluded."
        });
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
        var requiresExact = querySpec.EndsWith("-exact", StringComparison.Ordinal) && !querySpec.EndsWith("-no-exact", StringComparison.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = requiresExact
            ? "SELECT term FROM short_terms WHERE length(term) = $length AND match_class = 0 GROUP BY term ORDER BY count(*) DESC, term LIMIT 1;"
            : "SELECT term FROM short_terms WHERE length(term) = $length GROUP BY term HAVING sum(CASE WHEN match_class = 0 THEN 1 ELSE 0 END) = 0 ORDER BY count(*) DESC, term LIMIT 1;";
        command.Parameters.AddWithValue("$length", length);
        return (string?)command.ExecuteScalar() ?? throw new InvalidOperationException($"No {length}-character query satisfies '{querySpec}'.");
    }

    private static string ResolveCompactQuery(SqliteConnection connection, string querySpec)
    {
        if (!querySpec.StartsWith('@')) return querySpec;
        var length = querySpec.Contains("two", StringComparison.Ordinal) ? 2 : 1;
        var requiresExact = querySpec.EndsWith("-exact", StringComparison.Ordinal) && !querySpec.EndsWith("-no-exact", StringComparison.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = requiresExact
            ? "SELECT term FROM compact_terms WHERE length(term) = $length AND match_class = 0 ORDER BY posting_count DESC, term LIMIT 1;"
            : "SELECT term FROM compact_terms WHERE length(term) = $length GROUP BY term HAVING sum(CASE WHEN match_class = 0 THEN 1 ELSE 0 END) = 0 ORDER BY sum(posting_count) DESC, term LIMIT 1;";
        command.Parameters.AddWithValue("$length", length);
        return (string?)command.ExecuteScalar() ?? throw new InvalidOperationException($"No {length}-character compact query satisfies '{querySpec}'.");
    }

    private static long Percentile(IReadOnlyList<long> sorted, double percentile)
    {
        if (sorted.Count == 0) return 0;
        var index = Math.Clamp((int)Math.Ceiling(sorted.Count * percentile) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    private static double Share(IReadOnlyList<long> sorted, int count)
    {
        var total = sorted.Sum();
        if (total == 0) return 0;
        return Math.Round(sorted.TakeLast(Math.Min(count, sorted.Count)).Sum() * 100d / total, 3);
    }

    private static void CreateCompactSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE metadata(key TEXT PRIMARY KEY NOT NULL,value TEXT NOT NULL); CREATE TABLE compact_rank_map(id INTEGER PRIMARY KEY CHECK(id = 1),payload BLOB NOT NULL); CREATE TABLE compact_terms(term TEXT NOT NULL COLLATE NOCASE,match_class INTEGER NOT NULL,payload BLOB NOT NULL,posting_count INTEGER NOT NULL,PRIMARY KEY(term,match_class)) WITHOUT ROWID;";
        command.ExecuteNonQuery();
    }

    private static StaticRanks BuildStaticRanks(SqliteConnection source)
    {
        var mountPoint = ReadMountPoint(source);
        var nodes = new List<RankNode>();
        var byId = new Dictionary<NativeFileId, RankNode>();
        var maxRowId = 0;
        using (var command = source.CreateCommand())
        {
            command.CommandText = "SELECT rowid,file_id,parent_file_id,name,attributes FROM namespace_entries ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var node = new RankNode(
                    checked((int)reader.GetInt64(0)),
                    new NativeFileId((byte[])reader[1]),
                    new NativeFileId((byte[])reader[2]),
                    reader.GetString(3),
                    checked((uint)reader.GetInt64(4)));
                nodes.Add(node);
                byId.Add(node.FileId, node);
                maxRowId = Math.Max(maxRowId, node.RowId);
            }
        }

        string? ResolvePath(RankNode node)
        {
            if (node.PathResolved) return node.FullPath;
            node.PathResolved = true;
            if (node.FileId.Equals(node.ParentFileId)) return node.FullPath = mountPoint;
            if (!byId.TryGetValue(node.ParentFileId, out var parent)) return null;
            var parentPath = ResolvePath(parent);
            return parentPath is null ? null : node.FullPath = Path.Combine(parentPath, node.Name);
        }

        var context = FileSearchRankingContext.ForCurrentMachine();
        foreach (var node in nodes)
        {
            var path = ResolvePath(node);
            var result = new FileSearchResult(node.FileId, node.Name, path, (node.Attributes & 0x10) != 0, null, null, null, node.Attributes);
            var rank = FileSearchRanking.Classify(result, "rank", context);
            node.Location = rank.Location;
            node.PathDepth = rank.PathDepth;
            node.PathLength = rank.PathLength;
        }

        nodes.Sort(CompareStaticRank);
        var ordinalByRowId = new int[maxRowId + 1];
        var rowIdByOrdinal = new int[nodes.Count];
        var locationByOrdinal = new byte[nodes.Count];
        for (var ordinal = 0; ordinal < nodes.Count; ordinal++)
        {
            var node = nodes[ordinal];
            ordinalByRowId[node.RowId] = ordinal;
            rowIdByOrdinal[ordinal] = node.RowId;
            locationByOrdinal[ordinal] = checked((byte)node.Location);
        }

        return new StaticRanks(ordinalByRowId, rowIdByOrdinal, locationByOrdinal);
    }

    private static int CompareStaticRank(RankNode left, RankNode right)
    {
        var comparison = left.Location.CompareTo(right.Location);
        if (comparison != 0) return comparison;
        comparison = left.PathDepth.CompareTo(right.PathDepth);
        if (comparison != 0) return comparison;
        comparison = left.PathLength.CompareTo(right.PathLength);
        if (comparison != 0) return comparison;
        comparison = CompareUtf8(left.Name, right.Name, foldAscii: true);
        if (comparison != 0) return comparison;
        comparison = CompareUtf8(left.Name, right.Name, foldAscii: false);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(left.FullPath, right.FullPath);
        if (comparison != 0) return comparison;
        return StringComparer.Ordinal.Compare(left.FileId.ToString(), right.FileId.ToString());
    }

    private static int CompareUtf8(string left, string right, bool foldAscii)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        for (var index = 0; index < Math.Min(leftBytes.Length, rightBytes.Length); index++)
        {
            var leftByte = foldAscii && leftBytes[index] is >= (byte)'A' and <= (byte)'Z' ? (byte)(leftBytes[index] + 32) : leftBytes[index];
            var rightByte = foldAscii && rightBytes[index] is >= (byte)'A' and <= (byte)'Z' ? (byte)(rightBytes[index] + 32) : rightBytes[index];
            var comparison = leftByte.CompareTo(rightByte);
            if (comparison != 0) return comparison;
        }

        return leftBytes.Length.CompareTo(rightBytes.Length);
    }

    private static void WriteCompactRankMap(SqliteConnection connection, SqliteTransaction transaction, StaticRanks ranks)
    {
        var bytes = new byte[ranks.RowIdByOrdinal.Length * 5];
        for (var ordinal = 0; ordinal < ranks.RowIdByOrdinal.Length; ordinal++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(ordinal * 5, 4), ranks.RowIdByOrdinal[ordinal]);
            bytes[ordinal * 5 + 4] = ranks.LocationByOrdinal[ordinal];
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO compact_rank_map(id,payload) VALUES(1,$payload);";
        command.Parameters.AddWithValue("$payload", bytes);
        command.ExecuteNonQuery();
    }

    private static CompactRanks ReadCompactRankMap(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM compact_rank_map WHERE id = 1;";
        var bytes = (byte[]?)command.ExecuteScalar() ?? throw new InvalidOperationException("Compact rank map is missing.");
        if (bytes.Length % 5 != 0) throw new InvalidOperationException("Compact rank map has an invalid length.");
        var count = bytes.Length / 5;
        var rowIds = new int[count];
        var locations = new byte[count];
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            rowIds[ordinal] = BitConverter.ToInt32(bytes, ordinal * 5);
            locations[ordinal] = bytes[ordinal * 5 + 4];
        }

        using var spacingCommand = connection.CreateCommand();
        spacingCommand.CommandText = "SELECT value FROM metadata WHERE key = 'rank_label_spacing';";
        var spacing = int.Parse((string?)spacingCommand.ExecuteScalar() ?? "1", System.Globalization.CultureInfo.InvariantCulture);
        return new CompactRanks(rowIds, locations, bytes.Length, spacing);
    }

    private static byte[] EncodeDeltas(IReadOnlyList<int> ordinals)
    {
        using var stream = new MemoryStream();
        var previous = 0;
        foreach (var ordinal in ordinals)
        {
            WriteVarint(stream, checked((uint)(ordinal - previous)));
            previous = ordinal;
        }

        return stream.ToArray();
    }

    private static void WriteVarint(Stream stream, uint value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static IReadOnlyList<int> ReadCompactTopRowIds(SqliteConnection connection, CompactRanks ranks, string query, int limit)
    {
        var cursors = new List<PostingCursor>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT match_class,payload FROM compact_terms WHERE term = $query ORDER BY match_class;";
            command.Parameters.AddWithValue("$query", query);
            using var reader = command.ExecuteReader();
            while (reader.Read()) cursors.Add(new PostingCursor(reader.GetInt32(0), (byte[])reader[1]));
        }

        return MergeTopOrdinals(ranks, cursors.Select(cursor => (cursor.MatchClass, cursor.Payload)).ToArray(), limit)
            .Select(ordinal => ranks.RowIdByOrdinal[ordinal])
            .ToArray();
    }

    private static IReadOnlyList<int> MergeTopOrdinals(CompactRanks ranks, IReadOnlyList<(int MatchClass, byte[] Payload)> postings, int limit)
    {
        var cursors = postings.Select(posting => new PostingCursor(posting.MatchClass, posting.Payload)).ToList();
        foreach (var cursor in cursors) cursor.MoveNext();
        var result = new List<int>(limit);
        while (result.Count < limit)
        {
            var current = cursors.Where(cursor => cursor.HasValue).MinBy(cursor =>
            {
                var ordinal = ranks.OrdinalForLabel(cursor.Ordinal);
                return (long)ranks.LocationByOrdinal[ordinal] * 10_000_000L + (long)cursor.MatchClass * 1_000_000L + ordinal;
            });
            if (current is null) break;
            result.Add(ranks.OrdinalForLabel(current.Ordinal));
            current.MoveNext();
        }

        return result;
    }

    private static long CompactPostingCount(SqliteConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT coalesce(sum(posting_count), 0) FROM compact_terms WHERE term = $query;";
        command.Parameters.AddWithValue("$query", query);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static NativeFileId ReadFileId(SqliteConnection connection, int rowId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_id FROM namespace_entries WHERE rowid = $rowid;";
        command.Parameters.AddWithValue("$rowid", rowId);
        return new NativeFileId((byte[]?)command.ExecuteScalar() ?? throw new InvalidOperationException("Compact rowid is absent from the source index."));
    }

    private static string FingerprintIds(IEnumerable<NativeFileId> ids)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('|', ids.Select(id => id.ToString())));
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void SetCompactMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata(key,value) VALUES($key,$value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
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

    private sealed class RankNode(int rowId, NativeFileId fileId, NativeFileId parentFileId, string name, uint attributes)
    {
        public int RowId { get; } = rowId;
        public NativeFileId FileId { get; } = fileId;
        public NativeFileId ParentFileId { get; } = parentFileId;
        public string Name { get; } = name;
        public uint Attributes { get; } = attributes;
        public string? FullPath { get; set; }
        public bool PathResolved { get; set; }
        public FileSearchLocation Location { get; set; }
        public int PathDepth { get; set; }
        public int PathLength { get; set; }
    }

    private sealed record StaticRanks(int[] OrdinalByRowId, int[] RowIdByOrdinal, byte[] LocationByOrdinal);

    private sealed record CompactRanks(int[] RowIdByOrdinal, byte[] LocationByOrdinal, int SerializedBytes, int RankLabelSpacing = 1)
    {
        public int OrdinalForLabel(int label)
        {
            if (label < 0 || label % RankLabelSpacing != 0) throw new InvalidOperationException("Compact rank label is invalid.");
            var ordinal = label / RankLabelSpacing;
            if ((uint)ordinal >= (uint)RowIdByOrdinal.Length) throw new InvalidOperationException("Compact rank label is out of range.");
            return ordinal;
        }
    }

    private sealed class PostingCursor
    {
        private readonly byte[] _payload;
        private int _offset;
        private int _previous;

        public PostingCursor(int matchClass, byte[] payload)
        {
            MatchClass = matchClass;
            _payload = payload;
        }

        public int MatchClass { get; }
        public byte[] Payload => _payload;
        public int Ordinal { get; private set; }
        public bool HasValue { get; private set; }

        public void MoveNext()
        {
            if (_offset >= _payload.Length)
            {
                HasValue = false;
                return;
            }

            uint value = 0;
            var shift = 0;
            while (true)
            {
                if (_offset >= _payload.Length || shift > 28) throw new InvalidOperationException("Compact posting payload is invalid.");
                var next = _payload[_offset++];
                value |= (uint)(next & 0x7f) << shift;
                if ((next & 0x80) == 0) break;
                shift += 7;
            }

            Ordinal = checked(_previous + (int)value);
            _previous = Ordinal;
            HasValue = true;
        }
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

    private sealed record Options(string Command, string SourcePath, string OutputPath, string EvidencePath, string? DensePath, int RankLabelSpacing, int Limit, int Repetitions, IReadOnlyList<string> Queries, IReadOnlyDictionary<string, string> QueryKinds)
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
                values.TryGetValue("dense", out var dense) ? Path.GetFullPath(dense) : null,
                values.TryGetValue("label-spacing", out var labelSpacing) ? int.Parse(labelSpacing) : 1,
                values.TryGetValue("limit", out var limit) ? int.Parse(limit) : DefaultLimit,
                values.TryGetValue("repetitions", out var repetitions) ? int.Parse(repetitions) : 1,
                queries,
                queryPairs.Where(pair => pair.Length == 2).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal));
        }
    }
}
