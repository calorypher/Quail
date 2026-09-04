using System.Diagnostics;
using System.Buffers.Binary;
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
                "profile-search" => ProfileSearch(options),
                "snapshot" => WriteSnapshot(options),
                "compare" => CompareSnapshots(options),
                "inspect-index" => InspectIndex(options),
                _ => throw new ArgumentException("Command must be report, decompose, search, profile-search, snapshot, compare, or inspect-index.")
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

    private static int ProfileSearch(Options options)
    {
        var indexPath = Require(options, "index");
        var outputPath = Require(options, "output");
        var queries = Require(options, "queries")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (queries.Length == 0 || queries.Any(query => query.Length is < 1 or > 2))
        {
            throw new ArgumentException("--queries must contain one- or two-character values.");
        }

        using var connection = OpenReadOnly(indexPath);
        var context = FileSearchRankingContext.ForCurrentMachine();

        Collect();
        var rankMemoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var rankLoad = Stopwatch.StartNew();
        var ranks = ProfileRankMap.Read(connection);
        rankLoad.Stop();
        Collect();
        var rankMemoryAfter = GC.GetTotalMemory(forceFullCollection: false);

        var contextResolution = Stopwatch.StartNew();
        var contextInfo = ResolveProfileContext(connection, ranks, context);
        contextResolution.Stop();

        var fastContextResolution = Stopwatch.StartNew();
        var fastContextInfo = ResolveProfileContextFast(connection, ranks, context);
        fastContextResolution.Stop();
        if (contextInfo.CurrentUserLabel != fastContextInfo.CurrentUserLabel ||
            !contextInfo.SystemRootLabels.SetEquals(fastContextInfo.SystemRootLabels))
        {
            throw new InvalidOperationException("Optimized context resolution changed ranking context.");
        }

        var locationBuild = Stopwatch.StartNew();
        var authoritativeLocationByRankIndex = new byte[ranks.Count];
        for (var index = 0; index < ranks.Count; index++)
        {
            authoritativeLocationByRankIndex[index] = (byte)ClassifyProfileLocation(ranks[index], ranks, contextInfo);
        }
        locationBuild.Stop();

        var locationAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var optimizedLocationBuild = Stopwatch.StartNew();
        var locationByRankIndex = BuildProfileLocationMap(ranks, fastContextInfo);
        optimizedLocationBuild.Stop();
        var locationAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - locationAllocatedBefore;
        if (!authoritativeLocationByRankIndex.SequenceEqual(locationByRankIndex))
        {
            var mismatch = Enumerable.Range(0, ranks.Count)
                .First(index => authoritativeLocationByRankIndex[index] != locationByRankIndex[index]);
            var entry = ranks[mismatch];
            throw new InvalidOperationException(
                $"Optimized runtime location map changed location classification at index {mismatch}, label {entry.Label}, parent {entry.ParentLabel}, depth {entry.Depth}, flags {entry.Flags}, attributes {entry.Attributes}: expected {authoritativeLocationByRankIndex[mismatch]}, actual {locationByRankIndex[mismatch]}.");
        }

        var samples = new List<object>();
        foreach (var query in queries)
        {
            var authoritativeSearch = Stopwatch.StartNew();
            var authoritative = ShortQueryIndex.SearchAuthoritative(connection, query, 50, context);
            authoritativeSearch.Stop();

            Collect();
            var productionMemoryBefore = GC.GetTotalMemory(forceFullCollection: true);
            var productionAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var production = Stopwatch.StartNew();
            var productionResults = ShortQueryIndex.Search(connection, query, 50, context);
            production.Stop();
            var productionAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - productionAllocatedBefore;
            var productionMemoryAfter = GC.GetTotalMemory(forceFullCollection: false);
            Collect();
            var productionRetainedMemoryAfter = GC.GetTotalMemory(forceFullCollection: false);

            var postingRead = Stopwatch.StartNew();
            var payloads = ReadProfilePostingPayloads(connection, query);
            postingRead.Stop();

            var postingCount = payloads.Sum(payload => payload.PostingCount);
            var labels = new long[postingCount];
            var matchClasses = new byte[postingCount];
            var decode = Stopwatch.StartNew();
            var position = 0;
            foreach (var payload in payloads)
            {
                long previous = 0;
                var offset = 0;
                while (offset < payload.Payload.Length)
                {
                    previous = checked(previous + (long)ReadVarint(payload.Payload, ref offset));
                    labels[position] = previous;
                    matchClasses[position] = checked((byte)payload.MatchClass);
                    position++;
                }
            }
            decode.Stop();
            if (position != postingCount) throw new InvalidOperationException("Posting count does not match decoded labels.");

            var rankIndices = new int[postingCount];
            var rankLookup = Stopwatch.StartNew();
            for (var index = 0; index < labels.Length; index++) rankIndices[index] = ranks.IndexOf(labels[index]);
            rankLookup.Stop();

            var classified = new byte[postingCount];
            var fullClassification = Stopwatch.StartNew();
            for (var index = 0; index < rankIndices.Length; index++)
            {
                classified[index] = (byte)ClassifyProfileLocation(ranks[rankIndices[index]], ranks, contextInfo);
            }
            fullClassification.Stop();

            var mapLookup = Stopwatch.StartNew();
            for (var index = 0; index < rankIndices.Length; index++) classified[index] = locationByRankIndex[rankIndices[index]];
            mapLookup.Stop();

            var reconstruction = Stopwatch.StartNew();
            var optimized = SelectAndReconstruct(connection, ranks, labels, matchClasses, classified, 50);
            reconstruction.Stop();

            var cachedSearch = Stopwatch.StartNew();
            var cached = SearchWithRuntimeMap(connection, query, 50, ranks, locationByRankIndex);
            cachedSearch.Stop();

            var authoritativeIds = authoritative.Select(result => result.FileId.ToString()).ToArray();
            var optimizedIds = optimized.Select(result => result.FileId.ToString()).ToArray();
            var cachedIds = cached.Select(result => result.FileId.ToString()).ToArray();
            samples.Add(new
            {
                queryLength = query.Length,
                postingCount,
                postingChunkCount = payloads.Count,
                authoritativeFullScanSearchMilliseconds = Milliseconds(authoritativeSearch),
                productionSearchMilliseconds = Milliseconds(production),
                productionAllocatedBytes,
                productionManagedMemoryDeltaBeforeCollectionBytes = productionMemoryAfter - productionMemoryBefore,
                productionRetainedManagedMemoryDeltaBytes = productionRetainedMemoryAfter - productionMemoryBefore,
                postingSqliteReadMilliseconds = Milliseconds(postingRead),
                postingDecodeMilliseconds = Milliseconds(decode),
                rankLabelLookupMilliseconds = Milliseconds(rankLookup),
                fullLocationClassificationMilliseconds = Milliseconds(fullClassification),
                runtimeLocationLookupMilliseconds = Milliseconds(mapLookup),
                selectionAndResultReconstructionMilliseconds = Milliseconds(reconstruction),
                cachedRuntimeMapSearchMilliseconds = Milliseconds(cachedSearch),
                projectedFirstSearchMilliseconds = Math.Round(
                    rankLoad.Elapsed.TotalMilliseconds + fastContextResolution.Elapsed.TotalMilliseconds +
                    optimizedLocationBuild.Elapsed.TotalMilliseconds + cachedSearch.Elapsed.TotalMilliseconds, 3),
                resultCount = authoritative.Count,
                productionMatchesAuthoritative = authoritativeIds.SequenceEqual(
                    productionResults.Select(result => result.FileId.ToString())),
                optimizedMatchesAuthoritative = authoritativeIds.SequenceEqual(optimizedIds),
                cachedMatchesAuthoritative = authoritativeIds.SequenceEqual(cachedIds)
            });
        }

        Write(outputPath, new
        {
            schemaVersion = 1,
            kind = "m17-production-short-query-runtime-profile",
            indexPath = Path.GetFullPath(indexPath),
            records = ranks.Count,
            rankMapLoadMilliseconds = Milliseconds(rankLoad),
            rankMapManagedMemoryDeltaBytes = rankMemoryAfter - rankMemoryBefore,
            contextResolutionMilliseconds = Milliseconds(contextResolution),
            optimizedContextResolutionMilliseconds = Milliseconds(fastContextResolution),
            repeatedWalkLocationMapBuildMilliseconds = Milliseconds(locationBuild),
            runtimeLocationMapBuildMilliseconds = Milliseconds(optimizedLocationBuild),
            runtimeLocationMapRawBytes = locationByRankIndex.LongLength,
            runtimeLocationMapBuildAllocatedBytes = locationAllocatedBytes,
            samples,
            note = "Diagnostic-only isolated decomposition. authoritativeFullScanSearchMilliseconds classifies every posting through the original parent-walk ranking oracle; productionSearchMilliseconds measures the current production path. Component timings use the same persisted payloads and ranking rules but are isolated passes, so they are not additive production instrumentation. Optimized context resolution scans the rank map once for all resolved rowids. The optimized location-map builder memoizes parent topology and is verified entry-for-entry against repeated production-equivalent classification. cachedRuntimeMapSearch uses a preloaded rank map and one-byte runtime map. All optimized result identities/order are compared with the authoritative path."
        });
        return 0;
    }

    private static List<ProfilePostingPayload> ReadProfilePostingPayloads(SqliteConnection connection, string query)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_class,posting_count,payload
            FROM short_query_posting_chunks
            WHERE term=$term COLLATE BINARY
            ORDER BY match_class,first_label;
            """;
        command.Parameters.AddWithValue("$term", CanonicalizeSqliteAsciiTerm(query));
        using var reader = command.ExecuteReader();
        var payloads = new List<ProfilePostingPayload>();
        while (reader.Read())
        {
            payloads.Add(new ProfilePostingPayload(reader.GetInt32(0), reader.GetInt32(1), (byte[])reader[2]));
        }

        return payloads;
    }

    private static IReadOnlyList<FileSearchResult> SearchWithRuntimeMap(
        SqliteConnection connection,
        string query,
        int limit,
        ProfileRankMap ranks,
        byte[] locations)
    {
        var selected = CreateSelection(limit);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT match_class,payload
            FROM short_query_posting_chunks
            WHERE term=$term COLLATE BINARY
            ORDER BY match_class,first_label;
            """;
        command.Parameters.AddWithValue("$term", CanonicalizeSqliteAsciiTerm(query));
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var matchClass = reader.GetInt32(0);
            var payload = (byte[])reader[1];
            long previous = 0;
            var offset = 0;
            while (offset < payload.Length)
            {
                previous = checked(previous + (long)ReadVarint(payload, ref offset));
                var rankIndex = ranks.IndexOf(previous);
                var bucket = selected[locations[rankIndex], matchClass];
                if (bucket.Count < limit) bucket.Add(previous);
            }
        }

        return ReconstructSelection(connection, ranks, selected, limit);
    }

    private static IReadOnlyList<FileSearchResult> SelectAndReconstruct(
        SqliteConnection connection,
        ProfileRankMap ranks,
        long[] labels,
        byte[] matchClasses,
        byte[] locations,
        int limit)
    {
        var selected = CreateSelection(limit);
        for (var index = 0; index < labels.Length; index++)
        {
            var bucket = selected[locations[index], matchClasses[index]];
            if (bucket.Count < limit) bucket.Add(labels[index]);
        }

        return ReconstructSelection(connection, ranks, selected, limit);
    }

    private static List<long>[,] CreateSelection(int limit)
    {
        var selected = new List<long>[7, 4];
        for (var location = 0; location < 7; location++)
        {
            for (var match = 0; match < 4; match++) selected[location, match] = new List<long>(limit);
        }

        return selected;
    }

    private static IReadOnlyList<FileSearchResult> ReconstructSelection(
        SqliteConnection connection,
        ProfileRankMap ranks,
        List<long>[,] selected,
        int limit)
    {
        var results = new List<FileSearchResult>(limit);
        for (var location = 0; location < 7 && results.Count < limit; location++)
        {
            for (var match = 0; match < 4 && results.Count < limit; match++)
            {
                foreach (var label in selected[location, match])
                {
                    results.Add(ReadProfileResult(connection, ranks[ranks.IndexOf(label)].RowId));
                    if (results.Count == limit) break;
                }
            }
        }

        return results;
    }

    private static ProfileContext ResolveProfileContext(
        SqliteConnection connection,
        ProfileRankMap ranks,
        FileSearchRankingContext context)
    {
        var currentUser = ResolveProfilePathLabel(connection, ranks, context.CurrentUserProfilePath);
        var systemRoots = context.SystemRootPaths
            .Select(path => ResolveProfilePathLabel(connection, ranks, path))
            .Where(label => label is not null)
            .Select(label => label!.Value)
            .ToHashSet();
        return new ProfileContext(currentUser, systemRoots);
    }

    private static ProfileContext ResolveProfileContextFast(
        SqliteConnection connection,
        ProfileRankMap ranks,
        FileSearchRankingContext context)
    {
        var currentUserRowId = ResolveProfilePathRowId(connection, ranks, context.CurrentUserProfilePath);
        var systemRootRowIds = context.SystemRootPaths
            .Select(path => ResolveProfilePathRowId(connection, ranks, path))
            .Where(rowId => rowId is not null)
            .Select(rowId => rowId!.Value)
            .ToHashSet();
        var labelsByRowId = ranks.FindLabels(
            systemRootRowIds.Append(currentUserRowId ?? long.MinValue).ToHashSet());
        return new ProfileContext(
            currentUserRowId is long userRowId && labelsByRowId.TryGetValue(userRowId, out var userLabel) ? userLabel : null,
            systemRootRowIds.Where(labelsByRowId.ContainsKey).Select(rowId => labelsByRowId[rowId]).ToHashSet());
    }

    private static long? ResolveProfilePathRowId(SqliteConnection connection, ProfileRankMap ranks, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var mountSegments = FileSearchRankingContext.GetSegments(ScalarString(connection, "SELECT value FROM metadata WHERE key='mount_point';"));
        var pathSegments = FileSearchRankingContext.GetSegments(path);
        if (mountSegments.Count == 0 || pathSegments.Count < mountSegments.Count ||
            !mountSegments.SequenceEqual(pathSegments.Take(mountSegments.Count), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        NativeFileId parent;
        long rowId;
        using (var root = connection.CreateCommand())
        {
            rowId = ranks.Root.RowId;
            root.CommandText = "SELECT file_id FROM namespace_entries WHERE rowid=$rowid;";
            root.Parameters.AddWithValue("$rowid", rowId);
            using var reader = root.ExecuteReader();
            if (!reader.Read()) return null;
            parent = new NativeFileId((byte[])reader[0]);
        }

        for (var index = mountSegments.Count; index < pathSegments.Count; index++)
        {
            using var child = connection.CreateCommand();
            child.CommandText = "SELECT rowid,file_id,name FROM namespace_entries WHERE parent_file_id=$parent;";
            child.Parameters.Add("$parent", SqliteType.Blob).Value = parent.Bytes.ToArray();
            using var reader = child.ExecuteReader();
            var found = false;
            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(2), pathSegments[index], StringComparison.OrdinalIgnoreCase)) continue;
                rowId = reader.GetInt64(0);
                parent = new NativeFileId((byte[])reader[1]);
                found = true;
                break;
            }

            if (!found) return null;
        }

        return rowId;
    }

    private static byte[] BuildProfileLocationMap(ProfileRankMap ranks, ProfileContext context)
    {
        var locations = new byte[ranks.Count];
        var status = new byte[ranks.Count];
        var dynamicSystem = new bool[ranks.Count];
        var userZone = new byte[ranks.Count];
        var appDataSubtree = new bool[ranks.Count];
        var systemRootIndices = context.SystemRootLabels.Select(ranks.IndexOf).ToHashSet();
        var currentUserIndex = context.CurrentUserLabel is long currentUserLabel ? ranks.IndexOf(currentUserLabel) : -1;
        var userParentIndex = currentUserIndex >= 0 && ranks[currentUserIndex].Depth >= 2
            ? ranks.IndexOf(ranks[currentUserIndex].ParentLabel)
            : -1;

        for (var index = 0; index < ranks.Count; index++) Resolve(index);
        return locations;

        void Resolve(int index)
        {
            if (status[index] == 2) return;
            if (status[index] == 1) throw new InvalidOperationException("Cycle detected in short-query rank topology.");
            status[index] = 1;
            var entry = ranks[index];
            var parentIndex = entry.ParentLabel == entry.Label ? -1 : ranks.IndexOf(entry.ParentLabel);
            if (parentIndex >= 0) Resolve(parentIndex);

            dynamicSystem[index] = systemRootIndices.Contains(index) || parentIndex >= 0 && dynamicSystem[parentIndex];
            if (index == currentUserIndex)
            {
                userZone[index] = 2;
            }
            else if (index == userParentIndex)
            {
                userZone[index] = 3;
            }
            else if (parentIndex >= 0 && userZone[parentIndex] is 2 or 3)
            {
                userZone[index] = userZone[parentIndex];
                appDataSubtree[index] = ranks[parentIndex].Depth == ranks[currentUserIndex].Depth
                    ? (entry.Flags & 1) != 0
                    : appDataSubtree[parentIndex];
            }

            var internalEntry = (entry.Attributes & 0x6) != 0 || appDataSubtree[index];
            locations[index] = (byte)((entry.Flags & 2) != 0 || dynamicSystem[index]
                ? FileSearchLocation.SystemHeavy
                : userZone[index] == 2
                    ? internalEntry ? FileSearchLocation.CurrentUserInternal : FileSearchLocation.CurrentUserVisible
                    : userZone[index] == 3
                        ? internalEntry ? FileSearchLocation.OtherUserInternal : FileSearchLocation.OtherUserVisible
                        : (entry.Attributes & 0x6) != 0 ? FileSearchLocation.OtherInternal : FileSearchLocation.OtherVisible);
            status[index] = 2;
        }
    }

    private static long? ResolveProfilePathLabel(SqliteConnection connection, ProfileRankMap ranks, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var mountSegments = FileSearchRankingContext.GetSegments(ScalarString(connection, "SELECT value FROM metadata WHERE key='mount_point';"));
        var pathSegments = FileSearchRankingContext.GetSegments(path);
        if (mountSegments.Count == 0 || pathSegments.Count < mountSegments.Count ||
            !mountSegments.SequenceEqual(pathSegments.Take(mountSegments.Count), StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        NativeFileId parent;
        long rowId;
        using (var root = connection.CreateCommand())
        {
            root.CommandText = "SELECT rowid,file_id FROM namespace_entries WHERE file_id=parent_file_id LIMIT 1;";
            using var reader = root.ExecuteReader();
            if (!reader.Read()) return null;
            rowId = reader.GetInt64(0);
            parent = new NativeFileId((byte[])reader[1]);
        }

        for (var index = mountSegments.Count; index < pathSegments.Count; index++)
        {
            using var child = connection.CreateCommand();
            child.CommandText = "SELECT rowid,file_id FROM namespace_entries WHERE parent_file_id=$parent AND name=$name COLLATE NOCASE LIMIT 1;";
            child.Parameters.Add("$parent", SqliteType.Blob).Value = parent.Bytes.ToArray();
            child.Parameters.AddWithValue("$name", pathSegments[index]);
            using var reader = child.ExecuteReader();
            if (!reader.Read()) return null;
            rowId = reader.GetInt64(0);
            parent = new NativeFileId((byte[])reader[1]);
        }

        return ranks.FindLabel(rowId);
    }

    private static FileSearchLocation ClassifyProfileLocation(
        ProfileRankEntry entry,
        ProfileRankMap ranks,
        ProfileContext context)
    {
        if ((entry.Flags & 2) != 0 || context.SystemRootLabels.Any(label => IsProfileUnder(entry, ranks, label)))
        {
            return FileSearchLocation.SystemHeavy;
        }

        if (context.CurrentUserLabel is long currentUserLabel)
        {
            var currentUser = ranks[ranks.IndexOf(currentUserLabel)];
            if (IsProfileUnder(entry, ranks, currentUserLabel))
            {
                return IsProfileInternal(entry, ranks, currentUser.Depth)
                    ? FileSearchLocation.CurrentUserInternal
                    : FileSearchLocation.CurrentUserVisible;
            }

            if (currentUser.Depth >= 2 && IsProfileUnder(entry, ranks, currentUser.ParentLabel))
            {
                return IsProfileInternal(entry, ranks, currentUser.Depth)
                    ? FileSearchLocation.OtherUserInternal
                    : FileSearchLocation.OtherUserVisible;
            }
        }

        return (entry.Attributes & 0x6) != 0
            ? FileSearchLocation.OtherInternal
            : FileSearchLocation.OtherVisible;
    }

    private static bool IsProfileUnder(ProfileRankEntry entry, ProfileRankMap ranks, long ancestorLabel)
    {
        var ancestor = ranks[ranks.IndexOf(ancestorLabel)];
        var current = entry;
        while (current.Depth > ancestor.Depth) current = ranks[ranks.IndexOf(current.ParentLabel)];
        return current.Depth == ancestor.Depth && current.Label == ancestorLabel;
    }

    private static bool IsProfileInternal(ProfileRankEntry entry, ProfileRankMap ranks, ushort userDepth)
    {
        if ((entry.Attributes & 0x6) != 0) return true;
        if (entry.Depth <= userDepth) return false;
        var current = entry;
        while (current.Depth > userDepth + 1) current = ranks[ranks.IndexOf(current.ParentLabel)];
        return (current.Flags & 1) != 0;
    }

    private static FileSearchResult ReadProfileResult(SqliteConnection connection, long rowId)
    {
        NativeFileId fileId;
        string name;
        uint attributes;
        long? logicalSize;
        long? lastWrite;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT file_id,name,attributes,logical_size,last_write_time_utc FROM namespace_entries WHERE rowid=$rowid;";
            command.Parameters.AddWithValue("$rowid", rowId);
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Short-query posting references a missing entry.");
            fileId = new NativeFileId((byte[])reader[0]);
            name = reader.GetString(1);
            attributes = checked((uint)reader.GetInt64(2));
            logicalSize = reader.IsDBNull(3) ? null : reader.GetInt64(3);
            lastWrite = reader.IsDBNull(4) ? null : reader.GetInt64(4);
        }

        var fullPath = ReconstructProfilePath(connection, fileId);
        var isDirectory = (attributes & 0x10) != 0;
        return new FileSearchResult(
            fileId,
            name,
            fullPath,
            isDirectory,
            isDirectory ? null : Path.GetExtension(name).TrimStart('.').ToLowerInvariant() is { Length: > 0 } extension ? extension : null,
            isDirectory ? null : logicalSize,
            lastWrite,
            attributes);
    }

    private static string ReconstructProfilePath(SqliteConnection connection, NativeFileId fileId)
    {
        var mount = ScalarString(connection, "SELECT value FROM metadata WHERE key='mount_point';") ?? string.Empty;
        var parts = new List<string>();
        var seen = new HashSet<NativeFileId>();
        var current = fileId;
        while (true)
        {
            if (!seen.Add(current)) throw new InvalidOperationException("Cycle detected in parent relationships.");
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT parent_file_id,name FROM namespace_entries WHERE file_id=$id;";
            command.Parameters.Add("$id", SqliteType.Blob).Value = current.Bytes.ToArray();
            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Missing parent or record.");
            var parent = new NativeFileId((byte[])reader[0]);
            if (parent.Equals(current)) break;
            parts.Add(reader.GetString(1));
            current = parent;
        }

        parts.Reverse();
        return parts.Count == 0 ? mount : Path.Combine([mount, .. parts]);
    }

    private static string CanonicalizeSqliteAsciiTerm(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (characters[index] is >= 'A' and <= 'Z') characters[index] = (char)(characters[index] + ('a' - 'A'));
        }

        return new string(characters);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double Milliseconds(Stopwatch stopwatch) => Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3);

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

    private static int InspectIndex(Options options)
    {
        var indexPath = Require(options, "index");
        using var connection = OpenReadOnly(indexPath);
        Write(Require(options, "output"), new
        {
            schemaVersion = 1,
            kind = "m17-index-integrity-inspection",
            indexPath = Path.GetFullPath(indexPath),
            metadata = ReadDiagnosticMetadata(connection),
            checkpoint = ReadCheckpoint(connection),
            integrity = ReadIntegrity(connection),
            note = "The supplied SQLite index was opened in read-only mode. No metadata or index state was changed."
        });
        return 0;
    }

    private static object ReadDiagnosticMetadata(SqliteConnection connection)
    {
        string? Value(string key) => ScalarString(connection, $"SELECT value FROM metadata WHERE key='{key}';");
        return new
        {
            buildState = Value("build_state"),
            rebuildReason = Value("rebuild_reason"),
            recordCount = Value("record_count"),
            completedUtc = Value("completed_utc"),
            lastRefreshedUtc = Value("last_refreshed_utc"),
            volumeIdentity = Value("volume_identity"),
            mountPoint = Value("mount_point"),
            journalId = Value("journal_id"),
            nextUsn = Value("next_usn"),
            journalFirstUsn = Value("journal_first_usn"),
            journalLowestValidUsn = Value("journal_lowest_valid_usn"),
            namespaceGeneration = Value("namespace_generation"),
            shortQueryGeneration = Value("short_query_generation"),
            shortQueryFormat = Value("short_query_format")
        };
    }

    private static IncrementalCheckpoint ReadCheckpoint(SqliteConnection connection)
    {
        var journalId = ScalarString(connection, "SELECT value FROM metadata WHERE key='journal_id';");
        var nextUsn = ScalarString(connection, "SELECT value FROM metadata WHERE key='next_usn';");
        var firstUsn = ScalarString(connection, "SELECT value FROM metadata WHERE key='journal_first_usn';");
        var lowestUsn = ScalarString(connection, "SELECT value FROM metadata WHERE key='journal_lowest_valid_usn';");
        if (!ulong.TryParse(journalId, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var id) ||
            !long.TryParse(nextUsn, out var next) ||
            !long.TryParse(firstUsn, out var first) ||
            !long.TryParse(lowestUsn, out var lowest))
        {
            throw new InvalidDataException("Index checkpoint metadata is invalid.");
        }

        return new IncrementalCheckpoint(id, next, first, lowest);
    }

    private static IndexIntegrity ReadIntegrity(SqliteConnection connection)
    {
        var namespaceEntries = new List<IntegrityNamespaceEntry>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT rowid,file_id,parent_file_id,name,attributes,usn,record_version FROM namespace_entries;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                namespaceEntries.Add(new IntegrityNamespaceEntry(
                    reader.GetInt64(0),
                    Convert.ToHexString((byte[])reader[1]),
                    Convert.ToHexString((byte[])reader[2]),
                    reader.GetString(3),
                    $"0x{checked((uint)reader.GetInt64(4)):X8}",
                    reader.GetInt64(5),
                    checked((ushort)reader.GetInt64(6))));
            }
        }

        var ranks = new List<IntegrityRankEntry>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT entry_count,payload FROM short_query_rank_chunks ORDER BY first_label;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var count = reader.GetInt32(0);
                var payload = (byte[])reader[1];
                if (payload.Length != count * 28) throw new InvalidDataException("Rank chunk payload is invalid.");
                for (var index = 0; index < count; index++)
                {
                    var offset = index * 28;
                    ranks.Add(new IntegrityRankEntry(
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8)),
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8)),
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 16, 8))));
                }
            }
        }

        var namespaceById = namespaceEntries.ToDictionary(entry => entry.FileId, StringComparer.Ordinal);
        var namespaceRowIds = namespaceEntries.Select(entry => entry.RowId).ToHashSet();
        var rankByRowId = ranks.ToDictionary(entry => entry.RowId);
        var rankLabels = ranks.Select(entry => entry.Label).ToHashSet();
        bool IsRooted(IntegrityNamespaceEntry entry)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = entry;
            while (seen.Add(current.FileId))
            {
                if (current.FileId == current.ParentFileId) return true;
                if (!namespaceById.TryGetValue(current.ParentFileId, out current)) return false;
            }

            return false;
        }

        var unrootedNamespaceEntries = namespaceEntries.Where(entry => !IsRooted(entry)).ToArray();
        var namespaceOrphans = namespaceEntries
            .Where(entry => entry.FileId != entry.ParentFileId && !namespaceById.ContainsKey(entry.ParentFileId))
            .Select(entry => new IntegrityOrphanEntry(
                entry.FileId,
                entry.ParentFileId,
                entry.Name,
                entry.Attributes,
                entry.Usn,
                entry.RecordVersion,
                rankByRowId.TryGetValue(entry.RowId, out var rank) ? rank.Label : null,
                rankByRowId.TryGetValue(entry.RowId, out rank) ? rank.ParentLabel : null,
                namespaceEntries
                    .Where(candidate => candidate.FileId[..12] == entry.ParentFileId[..12])
                    .Select(candidate => new IntegrityParentCandidate(
                        candidate.FileId,
                        candidate.ParentFileId,
                        candidate.Name,
                        candidate.Attributes,
                        candidate.Usn,
                        candidate.RecordVersion))
                    .ToArray()))
            .ToArray();
        var namespaceRowsMissingRanks = namespaceEntries
            .Where(entry => !rankByRowId.ContainsKey(entry.RowId))
            .Select(entry => entry.FileId)
            .ToArray();
        var rankRowsMissingNamespace = ranks
            .Where(entry => !namespaceRowIds.Contains(entry.RowId))
            .Select(entry => entry.RowId)
            .ToArray();
        var missingParentRankLabels = ranks
            .Where(entry => entry.ParentLabel != entry.Label && !rankLabels.Contains(entry.ParentLabel))
            .Select(entry => entry.Label)
            .ToArray();
        var parentLabelMismatches = namespaceEntries
            .Where(entry => entry.FileId != entry.ParentFileId && namespaceById.ContainsKey(entry.ParentFileId) && rankByRowId.ContainsKey(entry.RowId))
            .Where(entry =>
            {
                var parent = namespaceById[entry.ParentFileId];
                return !rankByRowId.TryGetValue(parent.RowId, out var parentRank) ||
                    rankByRowId[entry.RowId].ParentLabel != parentRank.Label;
            })
            .Select(entry => entry.FileId)
            .ToArray();

        var orderLabels = new List<long>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT entry_count,payload FROM short_query_rank_order_chunks ORDER BY first_sort_key;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var count = reader.GetInt32(0);
                var payload = (byte[])reader[1];
                if (payload.Length != count * sizeof(long)) throw new InvalidDataException("Rank-order chunk payload is invalid.");
                for (var index = 0; index < count; index++)
                {
                    orderLabels.Add(BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(index * sizeof(long), sizeof(long))));
                }
            }
        }

        long postingLabels = 0;
        long danglingPostingLabels = 0;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT posting_count,payload FROM short_query_posting_chunks;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var expected = reader.GetInt32(0);
                var payload = (byte[])reader[1];
                var offset = 0;
                long label = 0;
                var decoded = 0;
                while (offset < payload.Length)
                {
                    var delta = ReadVarint(payload, ref offset);
                    label = checked(label + (long)delta);
                    decoded++;
                    postingLabels++;
                    if (!rankLabels.Contains(label)) danglingPostingLabels++;
                }

                if (decoded != expected) throw new InvalidDataException("Posting chunk count is invalid.");
            }
        }

        return new IndexIntegrity(
            namespaceEntries.Count,
            ranks.Count,
            unrootedNamespaceEntries.Length,
            unrootedNamespaceEntries
                .Select(entry => new IntegrityParentCandidate(
                    entry.FileId,
                    entry.ParentFileId,
                    entry.Name,
                    entry.Attributes,
                    entry.Usn,
                    entry.RecordVersion))
                .Take(50)
                .ToArray(),
            namespaceOrphans.Length,
            namespaceOrphans.Take(20).ToArray(),
            namespaceRowsMissingRanks.Length,
            namespaceRowsMissingRanks.Take(20).ToArray(),
            rankRowsMissingNamespace.Length,
            rankRowsMissingNamespace.Take(20).ToArray(),
            missingParentRankLabels.Length,
            missingParentRankLabels.Take(20).ToArray(),
            parentLabelMismatches.Length,
            parentLabelMismatches.Take(20).ToArray(),
            orderLabels.Count,
            orderLabels.Count - orderLabels.Distinct().Count(),
            rankLabels.Except(orderLabels).Count(),
            orderLabels.Except(rankLabels).Count(),
            postingLabels,
            danglingPostingLabels);
    }

    private static void ClearDerivedState(SqliteConnection connection)
    {
        DropDerivedSchema(connection);
        ShortQueryIndex.CreateSchema(connection);
        IndexStore.RemoveUnrootedNamespaceEntries(connection);
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
    private sealed record IntegrityNamespaceEntry(
        long RowId,
        string FileId,
        string ParentFileId,
        string Name,
        string Attributes,
        long Usn,
        ushort RecordVersion);
    private sealed record IntegrityOrphanEntry(
        string FileId,
        string ParentFileId,
        string Name,
        string Attributes,
        long Usn,
        ushort RecordVersion,
        long? RankLabel,
        long? ParentLabel,
        IReadOnlyList<IntegrityParentCandidate> ParentRecordNumberCandidates);
    private sealed record IntegrityParentCandidate(
        string FileId,
        string ParentFileId,
        string Name,
        string Attributes,
        long Usn,
        ushort RecordVersion);
    private sealed record IntegrityRankEntry(long Label, long RowId, long ParentLabel);
    private sealed record IndexIntegrity(
        int NamespaceEntries,
        int RankEntries,
        int UnrootedNamespaceCount,
        IReadOnlyList<IntegrityParentCandidate> UnrootedNamespaceEntries,
        int NamespaceOrphanCount,
        IReadOnlyList<IntegrityOrphanEntry> NamespaceOrphans,
        int NamespaceRowsMissingRankCount,
        IReadOnlyList<string> NamespaceRowsMissingRankFileIds,
        int RankRowsMissingNamespaceCount,
        IReadOnlyList<long> RankRowsMissingNamespaceRowIds,
        int MissingParentRankLabelCount,
        IReadOnlyList<long> MissingParentRankLabels,
        int ParentLabelMismatchCount,
        IReadOnlyList<string> ParentLabelMismatchFileIds,
        int OrderLabels,
        int DuplicateOrderLabels,
        int RankLabelsMissingFromOrder,
        int OrderLabelsMissingFromRanks,
        long PostingLabels,
        long DanglingPostingLabels);
    private sealed record ProfilePostingPayload(int MatchClass, int PostingCount, byte[] Payload);
    private readonly record struct ProfileRankEntry(
        long Label,
        long RowId,
        long ParentLabel,
        ushort Depth,
        byte Flags,
        byte Attributes);
    private sealed record ProfileContext(long? CurrentUserLabel, IReadOnlySet<long> SystemRootLabels);

    private sealed class ProfileRankMap
    {
        private const long InitialLabelSpacing = 1L << 12;
        private const int RankEntryBytes = 28;
        private readonly ProfileRankEntry[] _entries;
        private readonly ProfileRankEntry _root;

        private ProfileRankMap(ProfileRankEntry[] entries)
        {
            _entries = entries;
            _root = entries[0];
            if (_root.ParentLabel != _root.Label) throw new InvalidOperationException("Short-query rank map root is invalid.");
        }

        public int Count => _entries.Length;
        public ProfileRankEntry Root => _root;
        public ProfileRankEntry this[int index] => _entries[index];

        public static ProfileRankMap Read(SqliteConnection connection)
        {
            var entries = new List<ProfileRankEntry>();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT payload,entry_count FROM short_query_rank_chunks ORDER BY first_label;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var payload = (byte[])reader[0];
                var count = reader.GetInt32(1);
                if (payload.Length != checked(count * RankEntryBytes))
                {
                    throw new InvalidOperationException("Short-query rank map payload is invalid.");
                }

                for (var index = 0; index < count; index++)
                {
                    var offset = index * RankEntryBytes;
                    entries.Add(new ProfileRankEntry(
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8)),
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8)),
                        BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 16, 8)),
                        BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 24, 2)),
                        payload[offset + 26],
                        payload[offset + 27]));
                }
            }

            return new ProfileRankMap(entries.ToArray());
        }

        public int IndexOf(long label)
        {
            if (label % InitialLabelSpacing == 0)
            {
                var directIndex = label / InitialLabelSpacing - 1;
                if ((ulong)directIndex < (ulong)_entries.Length && _entries[directIndex].Label == label)
                {
                    return (int)directIndex;
                }
            }

            var low = 0;
            var high = _entries.Length - 1;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var comparison = _entries[middle].Label.CompareTo(label);
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }

            throw new InvalidOperationException("Short-query rank label is out of range.");
        }

        public long? FindLabel(long rowId)
        {
            foreach (var entry in _entries)
            {
                if (entry.RowId == rowId) return entry.Label;
            }

            return null;
        }

        public Dictionary<long, long> FindLabels(IReadOnlySet<long> rowIds)
        {
            var result = new Dictionary<long, long>();
            foreach (var entry in _entries)
            {
                if (rowIds.Contains(entry.RowId)) result.Add(entry.RowId, entry.Label);
            }

            return result;
        }
    }

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
