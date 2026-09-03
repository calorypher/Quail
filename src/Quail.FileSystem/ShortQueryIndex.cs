using System.Buffers.Binary;
using Microsoft.Data.Sqlite;

namespace Quail.FileSystem;

internal static class ShortQueryIndex
{
    // SQLite's built-in NOCASE/lower behavior is ASCII-only. Persist the same
    // ASCII-normalized representation under BINARY collation so build, lookup,
    // and incremental maintenance have one term identity without folding
    // non-ASCII literal substrings.
    internal const string Format = "compact-short-query-v2";
    private const long InitialLabelSpacing = 1L << 32;
    private const int ChunkEntryCount = 1_024;
    private const int RankEntryBytes = 28;
    private const uint InternalAttributes = 0x2 | 0x4;

    public static void CreateSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS short_query_posting_chunks(
                chunk_id INTEGER PRIMARY KEY,
                term TEXT NOT NULL COLLATE BINARY,
                match_class INTEGER NOT NULL,
                first_label INTEGER NOT NULL,
                last_label INTEGER NOT NULL,
                posting_count INTEGER NOT NULL,
                payload BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_short_query_posting_chunks_term
                ON short_query_posting_chunks(term,match_class,first_label);
            CREATE TABLE IF NOT EXISTS short_query_rank_chunks(
                chunk_id INTEGER PRIMARY KEY,
                first_label INTEGER NOT NULL,
                last_label INTEGER NOT NULL,
                entry_count INTEGER NOT NULL,
                payload BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_short_query_rank_chunks_label
                ON short_query_rank_chunks(first_label);
            CREATE TABLE IF NOT EXISTS short_query_rank_order_chunks(
                chunk_id INTEGER PRIMARY KEY,
                first_sort_key BLOB NOT NULL,
                last_sort_key BLOB NOT NULL,
                first_label INTEGER NOT NULL,
                last_label INTEGER NOT NULL,
                entry_count INTEGER NOT NULL,
                payload BLOB NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_short_query_rank_order_chunks_sort
                ON short_query_rank_order_chunks(first_sort_key);
            """;
        command.ExecuteNonQuery();
    }

    public static void Build(SqliteConnection connection)
    {
        var nodes = ReadNodes(connection);
        var byId = nodes.ToDictionary(node => node.FileId);
        var mountPoint = GetMeta(connection, "mount_point") ?? string.Empty;
        foreach (var node in nodes)
        {
            ResolvePath(node, byId, mountPoint, new HashSet<NativeFileId>());
            node.SortKey = CreateStaticSortKey(node);
        }

        nodes.Sort((left, right) => CompareBytes(left.SortKey!, right.SortKey!));
        for (var index = 0; index < nodes.Count; index++)
        {
            nodes[index].RankLabel = checked((index + 1L) * InitialLabelSpacing);
        }

        var labelById = nodes.ToDictionary(node => node.FileId, node => node.RankLabel);
        foreach (var node in nodes)
        {
            node.ParentLabel = labelById.GetValueOrDefault(node.ParentFileId, node.RankLabel);
            var result = new FileSearchResult(
                node.FileId,
                node.Name,
                node.FullPath,
                (node.Attributes & 0x10) != 0,
                null,
                null,
                null,
                node.Attributes);
            node.DefaultSystemHeavy = FileSearchRanking.Classify(
                result,
                "x",
                new FileSearchRankingContext(null)).Location == FileSearchLocation.SystemHeavy;
        }

        using var transaction = connection.BeginTransaction();
        InsertRankChunks(connection, transaction, nodes);
        InsertRankOrderChunks(connection, transaction, nodes);
        InsertPostingChunks(connection, transaction, nodes);
        SetMeta(connection, transaction, "short_query_format", Format);
        SetMeta(connection, transaction, "namespace_generation", "1");
        SetMeta(connection, transaction, "short_query_generation", "1");
        transaction.Commit();
    }

    public static bool IsCurrent(SqliteConnection connection) =>
        GetMeta(connection, "short_query_format") == Format &&
        GetMeta(connection, "namespace_generation") is string generation &&
        GetMeta(connection, "short_query_generation") == generation;

    public static void AdvanceGeneration(SqliteConnection connection, SqliteTransaction transaction)
    {
        if (!long.TryParse(GetMeta(connection, "namespace_generation"), out var generation) || generation < 1 || generation == long.MaxValue)
        {
            throw new InvalidOperationException("Short-query generation is invalid; rebuild is required.");
        }

        var next = (generation + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        SetMeta(connection, transaction, "namespace_generation", next);
        SetMeta(connection, transaction, "short_query_generation", next);
    }

    public static IReadOnlyList<FileSearchResult> Search(
        SqliteConnection connection,
        string query,
        int limit,
        FileSearchRankingContext context)
    {
        if (!IsCurrent(connection))
        {
            throw new InvalidOperationException("Short-query derived state is missing, stale, or incompatible; rebuild is required.");
        }

        var ranks = ReadRanks(connection);
        var contextInfo = ResolveContext(connection, ranks, context);
        var selected = new List<long>[7, 4];
        for (var location = 0; location < 7; location++)
        {
            for (var match = 0; match < 4; match++)
            {
                selected[location, match] = new List<long>(limit);
            }
        }

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
            if ((uint)matchClass >= 4) throw new InvalidOperationException("Short-query posting match class is invalid.");
            foreach (var label in DecodeLabels((byte[])reader[1]))
            {
                var entry = ranks.Get(label);
                var location = (int)ClassifyLocation(entry, ranks, contextInfo);
                var bucket = selected[location, matchClass];
                if (bucket.Count < limit)
                {
                    bucket.Add(label);
                }
            }
        }

        var results = new List<FileSearchResult>(limit);
        for (var location = 0; location < 7 && results.Count < limit; location++)
        {
            for (var match = 0; match < 4 && results.Count < limit; match++)
            {
                foreach (var label in selected[location, match])
                {
                    results.Add(ReadResult(connection, ranks.Get(label).RowId));
                    if (results.Count == limit) break;
                }
            }
        }

        return results;
    }

    public static IReadOnlyList<NativeFileId> ReadSubtreeIds(SqliteConnection connection, NativeFileId root)
    {
        var result = new List<NativeFileId>();
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE subtree(file_id,parent_file_id,depth) AS (
                SELECT file_id,parent_file_id,0 FROM namespace_entries WHERE file_id=$root
                UNION ALL
                SELECT child.file_id,child.parent_file_id,subtree.depth + 1
                FROM namespace_entries child JOIN subtree ON child.parent_file_id=subtree.file_id
                WHERE child.file_id != child.parent_file_id)
            SELECT file_id FROM subtree ORDER BY depth;
            """;
        command.Parameters.Add("$root", SqliteType.Blob).Value = root.Bytes.ToArray();
        using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(new NativeFileId((byte[])reader[0]));
        return result;
    }

    public static bool IsDirectory(SqliteConnection connection, NativeFileId fileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT attributes FROM namespace_entries WHERE file_id=$id;";
        command.Parameters.Add("$id", SqliteType.Blob).Value = fileId.Bytes.ToArray();
        return command.ExecuteScalar() is long attributes && (attributes & 0x10) != 0;
    }

    public static void RemoveCurrentEntry(SqliteConnection connection, SqliteTransaction transaction, NativeFileId fileId)
    {
        var node = ReadNode(connection, fileId);
        if (node is null) return;
        node.FullPath = ResolveNodePath(connection, node);
        node.SortKey = CreateStaticSortKey(node);
        var order = FindOrderEntry(connection, node.SortKey);
        if (order is null)
        {
            throw new InvalidOperationException("Short-query rank order is missing an authoritative entry.");
        }

        foreach (var term in GetTerms(node.Name))
        {
            RemovePostingLabel(connection, transaction, term, order.Label);
        }

        RemoveRankEntry(connection, transaction, order.Label);
        RemoveOrderEntry(connection, transaction, node.SortKey);
    }

    public static void InsertCurrentEntry(SqliteConnection connection, SqliteTransaction transaction, NativeFileId fileId)
    {
        var node = ReadNode(connection, fileId) ?? throw new InvalidOperationException("Short-query mutation references a missing entry.");
        node.FullPath = ResolveNodePath(connection, node);
        node.SortKey = CreateStaticSortKey(node);
        var (previous, next) = FindOrderNeighbors(connection, node.SortKey);
        var label = AllocateLabel(previous?.Label, next?.Label);
        var parentNode = node.FileId.Equals(node.ParentFileId)
            ? node
            : ReadNode(connection, node.ParentFileId) ?? throw new InvalidOperationException("Short-query mutation found a missing parent.");
        parentNode.FullPath ??= ResolveNodePath(connection, parentNode);
        parentNode.SortKey ??= CreateStaticSortKey(parentNode);
        node.RankLabel = label;
        node.ParentLabel = node.FileId.Equals(node.ParentFileId)
            ? label
            : FindOrderEntry(connection, parentNode.SortKey)?.Label ?? throw new InvalidOperationException("Short-query mutation found a missing parent rank.");
        var result = new FileSearchResult(node.FileId, node.Name, node.FullPath, (node.Attributes & 0x10) != 0, null, null, null, node.Attributes);
        node.DefaultSystemHeavy = FileSearchRanking.Classify(result, "x", new FileSearchRankingContext(null)).Location == FileSearchLocation.SystemHeavy;
        InsertOrderEntry(connection, transaction, new OrderEntry(label, node.SortKey));
        InsertRankEntry(connection, transaction, ToRankEntry(node));
        foreach (var term in GetTerms(node.Name))
        {
            InsertPostingLabel(connection, transaction, term, label);
        }
    }

    private static RankNode? ReadNode(SqliteConnection connection, NativeFileId fileId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid,file_id,parent_file_id,name,attributes FROM namespace_entries WHERE file_id=$id;";
        command.Parameters.Add("$id", SqliteType.Blob).Value = fileId.Bytes.ToArray();
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new RankNode(reader.GetInt64(0), new NativeFileId((byte[])reader[1]), new NativeFileId((byte[])reader[2]), reader.GetString(3), checked((uint)reader.GetInt64(4)))
            : null;
    }

    private static string ResolveNodePath(SqliteConnection connection, RankNode node)
    {
        var mount = GetMeta(connection, "mount_point") ?? string.Empty;
        var parts = new List<string>();
        var current = node;
        var seen = new HashSet<NativeFileId>();
        while (true)
        {
            if (!seen.Add(current.FileId)) throw new InvalidOperationException("Short-query mutation found a parent cycle.");
            if (!string.IsNullOrEmpty(current.Name)) parts.Add(current.Name);
            if (current.FileId.Equals(current.ParentFileId)) break;
            current = ReadNode(connection, current.ParentFileId) ?? throw new InvalidOperationException("Short-query mutation found a missing parent.");
        }

        parts.Reverse();
        return Path.Combine(mount, Path.Combine(parts.ToArray()));
    }

    private static RankEntry ToRankEntry(RankNode node)
    {
        return new RankEntry(
            node.RankLabel,
            node.RowId,
            node.ParentLabel,
            checked((ushort)FileSearchRankingContext.GetSegments(node.FullPath).Count),
            (byte)((string.Equals(node.Name, "AppData", StringComparison.OrdinalIgnoreCase) ? 1 : 0) | (node.DefaultSystemHeavy ? 2 : 0)),
            (byte)(node.Attributes & InternalAttributes));
    }

    private static long AllocateLabel(long? previous, long? next)
    {
        if (previous is null)
        {
            if (next is null) return InitialLabelSpacing;
            if (next <= 1) throw new InvalidOperationException("Short-query rank label gap is exhausted; rebuild is required.");
            return next.Value / 2;
        }

        if (next is null)
        {
            if (previous > long.MaxValue - InitialLabelSpacing) throw new InvalidOperationException("Short-query rank label range is exhausted; rebuild is required.");
            return previous.Value + InitialLabelSpacing;
        }

        if (next <= previous + 1) throw new InvalidOperationException("Short-query rank label gap is exhausted; rebuild is required.");
        return previous.Value + (next.Value - previous.Value) / 2;
    }

    private static List<RankNode> ReadNodes(SqliteConnection connection)
    {
        var nodes = new List<RankNode>();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT rowid,file_id,parent_file_id,name,attributes FROM namespace_entries ORDER BY rowid;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            nodes.Add(new RankNode(
                reader.GetInt64(0),
                new NativeFileId((byte[])reader[1]),
                new NativeFileId((byte[])reader[2]),
                reader.GetString(3),
                checked((uint)reader.GetInt64(4))));
        }

        return nodes;
    }

    private static string ResolvePath(
        RankNode node,
        IReadOnlyDictionary<NativeFileId, RankNode> byId,
        string mountPoint,
        HashSet<NativeFileId> resolving)
    {
        if (node.FullPath is not null) return node.FullPath;
        if (!resolving.Add(node.FileId))
        {
            throw new InvalidOperationException("Short-query rank construction found a parent cycle.");
        }

        try
        {
            if (node.FileId.Equals(node.ParentFileId))
            {
                return node.FullPath = mountPoint;
            }

            if (!byId.TryGetValue(node.ParentFileId, out var parent))
            {
                return node.FullPath = Path.Combine(mountPoint, "<unresolved-" + node.FileId + ">");
            }

            return node.FullPath = Path.Combine(ResolvePath(parent, byId, mountPoint, resolving), node.Name);
        }
        catch (InvalidOperationException) when (node.FullPath is null)
        {
            return node.FullPath = Path.Combine(mountPoint, "<unresolved-" + node.FileId + ">");
        }
        finally
        {
            resolving.Remove(node.FileId);
        }
    }

    private static byte[] CreateStaticSortKey(RankNode node)
    {
        var path = node.FullPath ?? throw new InvalidOperationException("Rank path is missing.");
        var depth = FileSearchRankingContext.GetSegments(path).Count;
        using var stream = new MemoryStream();
        WriteBigEndian(stream, depth);
        WriteBigEndian(stream, path.Length);
        WriteTerminated(stream, Utf8ComparisonBytes(node.Name, true));
        WriteTerminated(stream, Utf8ComparisonBytes(node.Name, false));
        WriteUtf16Ordinal(stream, path);
        stream.Write(node.FileId.Bytes.Span);
        return stream.ToArray();
    }

    private static byte[] Utf8ComparisonBytes(string value, bool foldAscii)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (foldAscii)
        {
            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] is >= (byte)'A' and <= (byte)'Z') bytes[index] += (byte)('a' - 'A');
            }
        }

        return bytes;
    }

    private static void WriteBigEndian(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private static void WriteTerminated(Stream stream, ReadOnlySpan<byte> value)
    {
        stream.Write(value);
        stream.WriteByte(0);
    }

    private static void WriteUtf16Ordinal(Stream stream, string value)
    {
        Span<byte> bytes = stackalloc byte[2];
        foreach (var character in value)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes, character);
            stream.Write(bytes);
        }

        stream.WriteByte(0);
        stream.WriteByte(0);
    }

    private static int CompareBytes(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        var length = Math.Min(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0) return comparison;
        }

        return left.Length.CompareTo(right.Length);
    }

    private static void InsertRankChunks(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<RankNode> nodes)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO short_query_rank_chunks(chunk_id,first_label,last_label,entry_count,payload) VALUES($id,$first,$last,$count,$payload);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var first = insert.Parameters.Add("$first", SqliteType.Integer);
        var last = insert.Parameters.Add("$last", SqliteType.Integer);
        var count = insert.Parameters.Add("$count", SqliteType.Integer);
        var payload = insert.Parameters.Add("$payload", SqliteType.Blob);
        var chunkId = 0;
        for (var start = 0; start < nodes.Count; start += ChunkEntryCount)
        {
            var entries = nodes.Skip(start).Take(ChunkEntryCount).ToArray();
            id.Value = chunkId++;
            first.Value = entries[0].RankLabel;
            last.Value = entries[^1].RankLabel;
            count.Value = entries.Length;
            payload.Value = EncodeRankEntries(entries);
            insert.ExecuteNonQuery();
        }
    }

    private static byte[] EncodeRankEntries(IReadOnlyList<RankNode> entries)
    {
        return EncodeRankEntries(entries.Select(ToRankEntry).ToArray());
    }

    private static byte[] EncodeRankEntries(IReadOnlyList<RankEntry> entries)
    {
        var payload = new byte[entries.Count * RankEntryBytes];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var offset = index * RankEntryBytes;
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset, 8), entry.Label);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset + 8, 8), entry.RowId);
            BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(offset + 16, 8), entry.ParentLabel);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 24, 2), entry.Depth);
            payload[offset + 26] = entry.Flags;
            payload[offset + 27] = entry.Attributes;
        }

        return payload;
    }

    private static void InsertRankOrderChunks(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<RankNode> nodes)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO short_query_rank_order_chunks(chunk_id,first_sort_key,last_sort_key,first_label,last_label,entry_count,payload) VALUES($id,$firstKey,$lastKey,$firstLabel,$lastLabel,$count,$payload);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var firstKey = insert.Parameters.Add("$firstKey", SqliteType.Blob);
        var lastKey = insert.Parameters.Add("$lastKey", SqliteType.Blob);
        var firstLabel = insert.Parameters.Add("$firstLabel", SqliteType.Integer);
        var lastLabel = insert.Parameters.Add("$lastLabel", SqliteType.Integer);
        var count = insert.Parameters.Add("$count", SqliteType.Integer);
        var payload = insert.Parameters.Add("$payload", SqliteType.Blob);
        var chunkId = 0;
        for (var start = 0; start < nodes.Count; start += ChunkEntryCount)
        {
            var entries = nodes.Skip(start).Take(ChunkEntryCount).ToArray();
            id.Value = chunkId++;
            firstKey.Value = entries[0].SortKey!;
            lastKey.Value = entries[^1].SortKey!;
            firstLabel.Value = entries[0].RankLabel;
            lastLabel.Value = entries[^1].RankLabel;
            count.Value = entries.Length;
            payload.Value = EncodeOrderEntries(entries.Select(entry => new OrderEntry(entry.RankLabel, entry.SortKey!)).ToArray());
            insert.ExecuteNonQuery();
        }
    }

    private static byte[] EncodeOrderEntries(IReadOnlyList<OrderEntry> entries)
    {
        using var stream = new MemoryStream();
        Span<byte> label = stackalloc byte[8];
        foreach (var entry in entries)
        {
            BinaryPrimitives.WriteInt64LittleEndian(label, entry.Label);
            stream.Write(label);
            WriteVarint(stream, checked((ulong)entry.SortKey.Length));
            stream.Write(entry.SortKey);
        }

        return stream.ToArray();
    }

    private static List<OrderEntry> DecodeOrderEntries(byte[] payload, int expectedCount)
    {
        var result = new List<OrderEntry>(expectedCount);
        var offset = 0;
        while (offset < payload.Length)
        {
            if (payload.Length - offset < 8) throw new InvalidOperationException("Short-query rank ordering payload is invalid.");
            var label = BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8));
            offset += 8;
            var length = ReadVarint(payload, ref offset);
            if (length > int.MaxValue || payload.Length - offset < (int)length) throw new InvalidOperationException("Short-query rank ordering payload is invalid.");
            var key = payload.AsSpan(offset, (int)length).ToArray();
            offset += (int)length;
            result.Add(new OrderEntry(label, key));
        }

        if (result.Count != expectedCount || result.Zip(result.Skip(1)).Any(pair => CompareBytes(pair.First.SortKey, pair.Second.SortKey) >= 0))
        {
            throw new InvalidOperationException("Short-query rank ordering payload is invalid.");
        }

        return result;
    }

    private static void InsertPostingChunks(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<RankNode> nodes)
    {
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = "INSERT INTO short_query_posting_chunks(chunk_id,term,match_class,first_label,last_label,posting_count,payload) VALUES($id,$term,$class,$first,$last,$count,$payload);";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var term = insert.Parameters.Add("$term", SqliteType.Text);
        var matchClass = insert.Parameters.Add("$class", SqliteType.Integer);
        var first = insert.Parameters.Add("$first", SqliteType.Integer);
        var last = insert.Parameters.Add("$last", SqliteType.Integer);
        var count = insert.Parameters.Add("$count", SqliteType.Integer);
        var payload = insert.Parameters.Add("$payload", SqliteType.Blob);
        var builders = new Dictionary<ShortTermKey, PostingBuilder>();
        var nextChunkId = 0;

        void Flush(ShortTermKey key, PostingBuilder builder)
        {
            if (builder.Labels.Count == 0) return;
            id.Value = nextChunkId++;
            term.Value = key.Term;
            matchClass.Value = (int)key.Match;
            first.Value = builder.Labels[0];
            last.Value = builder.Labels[^1];
            count.Value = builder.Labels.Count;
            payload.Value = EncodeLabels(builder.Labels);
            insert.ExecuteNonQuery();
            builder.Labels.Clear();
        }

        foreach (var node in nodes)
        {
            foreach (var key in GetTerms(node.Name))
            {
                if (!builders.TryGetValue(key, out var builder))
                {
                    builder = new PostingBuilder();
                    builders.Add(key, builder);
                }

                builder.Labels.Add(node.RankLabel);
                if (builder.Labels.Count == ChunkEntryCount) Flush(key, builder);
            }
        }

        foreach (var pair in builders)
        {
            Flush(pair.Key, pair.Value);
        }
    }

    private static IEnumerable<ShortTermKey> GetTerms(string name)
    {
        var classes = new Dictionary<string, FileSearchTextMatch>(StringComparer.Ordinal);
        for (var index = 0; index < name.Length; index++)
        {
            for (var length = 1; length <= 2 && index + length <= name.Length; length++)
            {
                var literalTerm = name.Substring(index, length);
                var term = CanonicalizeSqliteAsciiTerm(literalTerm);
                var match = FileSearchRanking.ClassifyTextMatch(name, literalTerm);
                if (!classes.TryGetValue(term, out var existing) || match < existing)
                {
                    classes[term] = match;
                }
            }
        }

        return classes.Select(pair => new ShortTermKey(pair.Key, pair.Value));
    }

    private static string CanonicalizeSqliteAsciiTerm(string term)
    {
        var firstUppercase = -1;
        for (var index = 0; index < term.Length; index++)
        {
            if (term[index] is >= 'A' and <= 'Z')
            {
                firstUppercase = index;
                break;
            }
        }

        if (firstUppercase < 0) return term;
        var characters = term.ToCharArray();
        for (var index = firstUppercase; index < characters.Length; index++)
        {
            if (characters[index] is >= 'A' and <= 'Z') characters[index] = (char)(characters[index] + ('a' - 'A'));
        }

        return new string(characters);
    }

    private static byte[] EncodeLabels(IReadOnlyList<long> labels)
    {
        using var stream = new MemoryStream();
        long previous = 0;
        foreach (var label in labels)
        {
            if (label <= previous) throw new InvalidOperationException("Short-query labels must be strictly increasing.");
            WriteVarint(stream, checked((ulong)(label - previous)));
            previous = label;
        }

        return stream.ToArray();
    }

    private static IEnumerable<long> DecodeLabels(byte[] payload)
    {
        long previous = 0;
        var offset = 0;
        while (offset < payload.Length)
        {
            var delta = ReadVarint(payload, ref offset);
            if (delta > long.MaxValue || previous > long.MaxValue - (long)delta)
            {
                throw new InvalidOperationException("Short-query posting payload is invalid.");
            }

            previous += (long)delta;
            yield return previous;
        }
    }

    private static void WriteVarint(Stream stream, ulong value)
    {
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
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

    private static RankMap ReadRanks(SqliteConnection connection)
    {
        var entries = new List<RankEntry>();
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
                entries.Add(new RankEntry(
                    BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8)),
                    BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8)),
                    BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 16, 8)),
                    BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 24, 2)),
                    payload[offset + 26],
                    payload[offset + 27]));
            }
        }

        return new RankMap(entries);
    }

    private static List<RankEntry> DecodeRankEntries(byte[] payload, int expectedCount)
    {
        if (payload.Length != checked(expectedCount * RankEntryBytes)) throw new InvalidOperationException("Short-query rank map payload is invalid.");
        var entries = new List<RankEntry>(expectedCount);
        for (var index = 0; index < expectedCount; index++)
        {
            var offset = index * RankEntryBytes;
            entries.Add(new RankEntry(
                BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset, 8)),
                BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 8, 8)),
                BinaryPrimitives.ReadInt64LittleEndian(payload.AsSpan(offset + 16, 8)),
                BinaryPrimitives.ReadUInt16LittleEndian(payload.AsSpan(offset + 24, 2)),
                payload[offset + 26],
                payload[offset + 27]));
        }

        return entries;
    }

    private static void InsertRankEntry(SqliteConnection connection, SqliteTransaction transaction, RankEntry entry)
    {
        var chunk = FindRankChunk(connection, entry.Label, insert: true);
        if (chunk is null)
        {
            InsertRankChunk(connection, transaction, NextChunkId(connection, "short_query_rank_chunks"), [entry]);
            return;
        }

        var entries = DecodeRankEntries(chunk.Payload, chunk.EntryCount);
        entries.Insert(entries.FindIndex(candidate => candidate.Label > entry.Label) is var position && position >= 0 ? position : entries.Count, entry);
        SaveOrSplitRankChunk(connection, transaction, chunk.ChunkId, entries);
    }

    private static void RemoveRankEntry(SqliteConnection connection, SqliteTransaction transaction, long label)
    {
        var chunk = FindRankChunk(connection, label, insert: false) ?? throw new InvalidOperationException("Short-query rank entry is missing.");
        var entries = DecodeRankEntries(chunk.Payload, chunk.EntryCount);
        if (!entries.RemoveAll(entry => entry.Label == label).Equals(1)) throw new InvalidOperationException("Short-query rank entry is missing.");
        if (entries.Count == 0)
        {
            DeleteChunk(connection, transaction, "short_query_rank_chunks", chunk.ChunkId);
        }
        else
        {
            SaveRankChunk(connection, transaction, chunk.ChunkId, entries);
        }
    }

    private static RankChunk? FindRankChunk(SqliteConnection connection, long label, bool insert)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT chunk_id,entry_count,payload FROM short_query_rank_chunks WHERE first_label <= $label ORDER BY first_label DESC LIMIT 1;";
        command.Parameters.AddWithValue("$label", label);
        using var reader = command.ExecuteReader();
        if (reader.Read()) return new RankChunk(reader.GetInt64(0), reader.GetInt32(1), (byte[])reader[2]);
        if (!insert) return null;
        using var first = connection.CreateCommand();
        first.CommandText = "SELECT chunk_id,entry_count,payload FROM short_query_rank_chunks ORDER BY first_label LIMIT 1;";
        using var firstReader = first.ExecuteReader();
        return firstReader.Read() ? new RankChunk(firstReader.GetInt64(0), firstReader.GetInt32(1), (byte[])firstReader[2]) : null;
    }

    private static void SaveOrSplitRankChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, List<RankEntry> entries)
    {
        if (entries.Count <= ChunkEntryCount)
        {
            SaveRankChunk(connection, transaction, chunkId, entries);
            return;
        }

        var split = entries.Count / 2;
        SaveRankChunk(connection, transaction, chunkId, entries.Take(split).ToList());
        InsertRankChunk(connection, transaction, NextChunkId(connection, "short_query_rank_chunks"), entries.Skip(split).ToList());
    }

    private static void SaveRankChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, IReadOnlyList<RankEntry> entries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE short_query_rank_chunks SET first_label=$first,last_label=$last,entry_count=$count,payload=$payload WHERE chunk_id=$id;";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.AddWithValue("$first", entries[0].Label);
        command.Parameters.AddWithValue("$last", entries[^1].Label);
        command.Parameters.AddWithValue("$count", entries.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeRankEntries(entries);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Short-query rank chunk is missing.");
    }

    private static void InsertRankChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, IReadOnlyList<RankEntry> entries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO short_query_rank_chunks(chunk_id,first_label,last_label,entry_count,payload) VALUES($id,$first,$last,$count,$payload);";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.AddWithValue("$first", entries[0].Label);
        command.Parameters.AddWithValue("$last", entries[^1].Label);
        command.Parameters.AddWithValue("$count", entries.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeRankEntries(entries);
        command.ExecuteNonQuery();
    }

    private static OrderEntry? FindOrderEntry(SqliteConnection connection, byte[] sortKey)
    {
        var chunk = FindOrderChunk(connection, sortKey, insert: false);
        if (chunk is null) return null;
        return DecodeOrderEntries(chunk.Payload, chunk.EntryCount).SingleOrDefault(entry => entry.SortKey.AsSpan().SequenceEqual(sortKey));
    }

    private static (OrderEntry? Previous, OrderEntry? Next) FindOrderNeighbors(SqliteConnection connection, byte[] sortKey)
    {
        var chunk = FindOrderChunk(connection, sortKey, insert: true);
        if (chunk is null) return (null, null);
        var entries = DecodeOrderEntries(chunk.Payload, chunk.EntryCount);
        var position = entries.FindIndex(entry => CompareBytes(entry.SortKey, sortKey) > 0);
        if (position >= 0)
        {
            return (position > 0 ? entries[position - 1] : PreviousOrderEntry(connection, chunk.FirstSortKey), entries[position]);
        }

        return (entries[^1], NextOrderEntry(connection, chunk.LastSortKey));
    }

    private static void InsertOrderEntry(SqliteConnection connection, SqliteTransaction transaction, OrderEntry entry)
    {
        var chunk = FindOrderChunk(connection, entry.SortKey, insert: true);
        if (chunk is null)
        {
            InsertOrderChunk(connection, transaction, NextChunkId(connection, "short_query_rank_order_chunks"), [entry]);
            return;
        }

        var entries = DecodeOrderEntries(chunk.Payload, chunk.EntryCount);
        var position = entries.FindIndex(candidate => CompareBytes(candidate.SortKey, entry.SortKey) > 0);
        entries.Insert(position < 0 ? entries.Count : position, entry);
        if (entries.Count <= ChunkEntryCount)
        {
            SaveOrderChunk(connection, transaction, chunk.ChunkId, entries);
            return;
        }

        var split = entries.Count / 2;
        SaveOrderChunk(connection, transaction, chunk.ChunkId, entries.Take(split).ToList());
        InsertOrderChunk(connection, transaction, NextChunkId(connection, "short_query_rank_order_chunks"), entries.Skip(split).ToList());
    }

    private static void RemoveOrderEntry(SqliteConnection connection, SqliteTransaction transaction, byte[] sortKey)
    {
        var chunk = FindOrderChunk(connection, sortKey, insert: false) ?? throw new InvalidOperationException("Short-query rank order is missing.");
        var entries = DecodeOrderEntries(chunk.Payload, chunk.EntryCount);
        if (entries.RemoveAll(entry => entry.SortKey.AsSpan().SequenceEqual(sortKey)) != 1) throw new InvalidOperationException("Short-query rank order is missing.");
        if (entries.Count == 0) DeleteChunk(connection, transaction, "short_query_rank_order_chunks", chunk.ChunkId);
        else SaveOrderChunk(connection, transaction, chunk.ChunkId, entries);
    }

    private static OrderChunk? FindOrderChunk(SqliteConnection connection, byte[] sortKey, bool insert)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT chunk_id,first_sort_key,last_sort_key,entry_count,payload FROM short_query_rank_order_chunks WHERE first_sort_key <= $key ORDER BY first_sort_key DESC LIMIT 1;";
        command.Parameters.Add("$key", SqliteType.Blob).Value = sortKey;
        using var reader = command.ExecuteReader();
        if (reader.Read()) return new OrderChunk(reader.GetInt64(0), (byte[])reader[1], (byte[])reader[2], reader.GetInt32(3), (byte[])reader[4]);
        if (!insert) return null;
        using var first = connection.CreateCommand();
        first.CommandText = "SELECT chunk_id,first_sort_key,last_sort_key,entry_count,payload FROM short_query_rank_order_chunks ORDER BY first_sort_key LIMIT 1;";
        using var firstReader = first.ExecuteReader();
        return firstReader.Read() ? new OrderChunk(firstReader.GetInt64(0), (byte[])firstReader[1], (byte[])firstReader[2], firstReader.GetInt32(3), (byte[])firstReader[4]) : null;
    }

    private static OrderEntry? PreviousOrderEntry(SqliteConnection connection, byte[] firstSortKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT entry_count,payload FROM short_query_rank_order_chunks WHERE last_sort_key < $key ORDER BY last_sort_key DESC LIMIT 1;";
        command.Parameters.Add("$key", SqliteType.Blob).Value = firstSortKey;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return DecodeOrderEntries((byte[])reader[1], reader.GetInt32(0))[^1];
    }

    private static OrderEntry? NextOrderEntry(SqliteConnection connection, byte[] lastSortKey)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT entry_count,payload FROM short_query_rank_order_chunks WHERE first_sort_key > $key ORDER BY first_sort_key LIMIT 1;";
        command.Parameters.Add("$key", SqliteType.Blob).Value = lastSortKey;
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        return DecodeOrderEntries((byte[])reader[1], reader.GetInt32(0))[0];
    }

    private static void SaveOrderChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, IReadOnlyList<OrderEntry> entries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE short_query_rank_order_chunks SET first_sort_key=$firstKey,last_sort_key=$lastKey,first_label=$firstLabel,last_label=$lastLabel,entry_count=$count,payload=$payload WHERE chunk_id=$id;";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.Add("$firstKey", SqliteType.Blob).Value = entries[0].SortKey;
        command.Parameters.Add("$lastKey", SqliteType.Blob).Value = entries[^1].SortKey;
        command.Parameters.AddWithValue("$firstLabel", entries[0].Label);
        command.Parameters.AddWithValue("$lastLabel", entries[^1].Label);
        command.Parameters.AddWithValue("$count", entries.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeOrderEntries(entries);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Short-query rank order chunk is missing.");
    }

    private static void InsertOrderChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, IReadOnlyList<OrderEntry> entries)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO short_query_rank_order_chunks(chunk_id,first_sort_key,last_sort_key,first_label,last_label,entry_count,payload) VALUES($id,$firstKey,$lastKey,$firstLabel,$lastLabel,$count,$payload);";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.Add("$firstKey", SqliteType.Blob).Value = entries[0].SortKey;
        command.Parameters.Add("$lastKey", SqliteType.Blob).Value = entries[^1].SortKey;
        command.Parameters.AddWithValue("$firstLabel", entries[0].Label);
        command.Parameters.AddWithValue("$lastLabel", entries[^1].Label);
        command.Parameters.AddWithValue("$count", entries.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeOrderEntries(entries);
        command.ExecuteNonQuery();
    }

    private static void InsertPostingLabel(SqliteConnection connection, SqliteTransaction transaction, ShortTermKey key, long label)
    {
        var chunk = FindPostingChunk(connection, key, label, insert: true);
        if (chunk is null)
        {
            InsertPostingChunk(connection, transaction, NextChunkId(connection, "short_query_posting_chunks"), key, [label]);
            return;
        }

        var labels = DecodeLabels(chunk.Payload).ToList();
        var position = labels.FindIndex(candidate => candidate > label);
        labels.Insert(position < 0 ? labels.Count : position, label);
        if (labels.Count <= ChunkEntryCount)
        {
            SavePostingChunk(connection, transaction, chunk.ChunkId, labels);
            return;
        }

        var split = labels.Count / 2;
        SavePostingChunk(connection, transaction, chunk.ChunkId, labels.Take(split).ToList());
        InsertPostingChunk(connection, transaction, NextChunkId(connection, "short_query_posting_chunks"), key, labels.Skip(split).ToList());
    }

    private static void RemovePostingLabel(SqliteConnection connection, SqliteTransaction transaction, ShortTermKey key, long label)
    {
        var chunk = FindPostingChunk(connection, key, label, insert: false) ?? throw new InvalidOperationException("Short-query posting is missing.");
        var labels = DecodeLabels(chunk.Payload).ToList();
        if (!labels.Remove(label)) throw new InvalidOperationException("Short-query posting is missing.");
        if (labels.Count == 0) DeleteChunk(connection, transaction, "short_query_posting_chunks", chunk.ChunkId);
        else SavePostingChunk(connection, transaction, chunk.ChunkId, labels);
    }

    private static PostingChunk? FindPostingChunk(SqliteConnection connection, ShortTermKey key, long label, bool insert)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT chunk_id,term,match_class,payload FROM short_query_posting_chunks WHERE term=$term COLLATE BINARY AND match_class=$class AND first_label <= $label ORDER BY first_label DESC LIMIT 1;";
        command.Parameters.AddWithValue("$term", key.Term);
        command.Parameters.AddWithValue("$class", (int)key.Match);
        command.Parameters.AddWithValue("$label", label);
        using var reader = command.ExecuteReader();
        if (reader.Read()) return new PostingChunk(reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2), (byte[])reader[3]);
        if (!insert) return null;
        using var first = connection.CreateCommand();
        first.CommandText = "SELECT chunk_id,term,match_class,payload FROM short_query_posting_chunks WHERE term=$term COLLATE BINARY AND match_class=$class ORDER BY first_label LIMIT 1;";
        first.Parameters.AddWithValue("$term", key.Term);
        first.Parameters.AddWithValue("$class", (int)key.Match);
        using var firstReader = first.ExecuteReader();
        return firstReader.Read() ? new PostingChunk(firstReader.GetInt64(0), firstReader.GetString(1), firstReader.GetInt32(2), (byte[])firstReader[3]) : null;
    }

    private static void SavePostingChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, IReadOnlyList<long> labels)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE short_query_posting_chunks SET first_label=$first,last_label=$last,posting_count=$count,payload=$payload WHERE chunk_id=$id;";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.AddWithValue("$first", labels[0]);
        command.Parameters.AddWithValue("$last", labels[^1]);
        command.Parameters.AddWithValue("$count", labels.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeLabels(labels);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Short-query posting chunk is missing.");
    }

    private static void InsertPostingChunk(SqliteConnection connection, SqliteTransaction transaction, long chunkId, ShortTermKey key, IReadOnlyList<long> labels)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO short_query_posting_chunks(chunk_id,term,match_class,first_label,last_label,posting_count,payload) VALUES($id,$term,$class,$first,$last,$count,$payload);";
        command.Parameters.AddWithValue("$id", chunkId);
        command.Parameters.AddWithValue("$term", key.Term);
        command.Parameters.AddWithValue("$class", (int)key.Match);
        command.Parameters.AddWithValue("$first", labels[0]);
        command.Parameters.AddWithValue("$last", labels[^1]);
        command.Parameters.AddWithValue("$count", labels.Count);
        command.Parameters.Add("$payload", SqliteType.Blob).Value = EncodeLabels(labels);
        command.ExecuteNonQuery();
    }

    private static long NextChunkId(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT coalesce(max(chunk_id), -1) + 1 FROM {table};";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void DeleteChunk(SqliteConnection connection, SqliteTransaction transaction, string table, long chunkId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"DELETE FROM {table} WHERE chunk_id=$id;";
        command.Parameters.AddWithValue("$id", chunkId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Short-query chunk is missing.");
    }

    private static ContextInfo ResolveContext(SqliteConnection connection, RankMap ranks, FileSearchRankingContext context)
    {
        var currentUser = ResolvePathLabel(connection, ranks, context.CurrentUserProfilePath);
        var systemRoots = context.SystemRootPaths
            .Select(path => ResolvePathLabel(connection, ranks, path))
            .Where(label => label is not null)
            .Select(label => label!.Value)
            .ToHashSet();
        return new ContextInfo(currentUser, systemRoots);
    }

    private static long? ResolvePathLabel(SqliteConnection connection, RankMap ranks, string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var mountSegments = FileSearchRankingContext.GetSegments(GetMeta(connection, "mount_point"));
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

    private static FileSearchLocation ClassifyLocation(RankEntry entry, RankMap ranks, ContextInfo context)
    {
        if ((entry.Flags & 2) != 0 || context.SystemRootLabels.Any(label => IsUnder(entry, ranks, label)))
        {
            return FileSearchLocation.SystemHeavy;
        }

        if (context.CurrentUserLabel is long currentUserLabel)
        {
            var currentUser = ranks.Get(currentUserLabel);
            if (IsUnder(entry, ranks, currentUserLabel))
            {
                return IsInternal(entry, ranks, currentUser.Depth)
                    ? FileSearchLocation.CurrentUserInternal
                    : FileSearchLocation.CurrentUserVisible;
            }

            if (currentUser.Depth >= 2 && IsUnder(entry, ranks, currentUser.ParentLabel))
            {
                return IsInternal(entry, ranks, currentUser.Depth)
                    ? FileSearchLocation.OtherUserInternal
                    : FileSearchLocation.OtherUserVisible;
            }
        }

        return (entry.Attributes & InternalAttributes) != 0
            ? FileSearchLocation.OtherInternal
            : FileSearchLocation.OtherVisible;
    }

    private static bool IsUnder(RankEntry entry, RankMap ranks, long ancestorLabel)
    {
        var ancestor = ranks.Get(ancestorLabel);
        var current = entry;
        while (current.Depth > ancestor.Depth)
        {
            current = ranks.Get(current.ParentLabel);
        }

        return current.Depth == ancestor.Depth && current.Label == ancestorLabel;
    }

    private static bool IsInternal(RankEntry entry, RankMap ranks, ushort userDepth)
    {
        if ((entry.Attributes & InternalAttributes) != 0) return true;
        if (entry.Depth <= userDepth) return false;
        var current = entry;
        while (current.Depth > userDepth + 1)
        {
            current = ranks.Get(current.ParentLabel);
        }

        return (current.Flags & 1) != 0;
    }

    private static FileSearchResult ReadResult(SqliteConnection connection, long rowId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT file_id,name,attributes,logical_size,last_write_time_utc FROM namespace_entries WHERE rowid=$rowid;";
        command.Parameters.AddWithValue("$rowid", rowId);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new InvalidOperationException("Short-query posting references a missing entry.");
        var fileId = new NativeFileId((byte[])reader[0]);
        var name = reader.GetString(1);
        var attributes = checked((uint)reader.GetInt64(2));
        var isDirectory = (attributes & 0x10) != 0;
        return new FileSearchResult(
            fileId,
            name,
            ReconstructPath(connection, fileId).Path,
            isDirectory,
            isDirectory ? null : GetExtension(name),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            attributes);
    }

    private static PathResolution ReconstructPath(SqliteConnection connection, NativeFileId fileId)
    {
        var mount = GetMeta(connection, "mount_point") ?? string.Empty;
        var parts = new List<string>();
        var seen = new HashSet<NativeFileId>();
        var current = fileId;
        while (true)
        {
            if (!seen.Add(current)) return new PathResolution(false, null, "Cycle detected in parent relationships.");
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT parent_file_id,name FROM namespace_entries WHERE file_id=$id;";
            command.Parameters.Add("$id", SqliteType.Blob).Value = current.Bytes.ToArray();
            using var reader = command.ExecuteReader();
            if (!reader.Read()) return new PathResolution(false, null, "Missing parent or record.");
            var parent = new NativeFileId((byte[])reader[0]);
            var name = reader.GetString(1);
            if (!string.IsNullOrEmpty(name)) parts.Add(name);
            if (parent.Equals(current)) break;
            current = parent;
        }

        parts.Reverse();
        return new PathResolution(true, Path.Combine(mount, Path.Combine(parts.ToArray())), null);
    }

    private static string? GetExtension(string name)
    {
        var extension = Path.GetExtension(name);
        return string.IsNullOrEmpty(extension) ? null : extension[1..];
    }

    private static string? GetMeta(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key=$key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static void SetMeta(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO metadata(key,value) VALUES($key,$value) ON CONFLICT(key) DO UPDATE SET value=excluded.value;";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private sealed class RankNode(long rowId, NativeFileId fileId, NativeFileId parentFileId, string name, uint attributes)
    {
        public long RowId { get; } = rowId;
        public NativeFileId FileId { get; } = fileId;
        public NativeFileId ParentFileId { get; } = parentFileId;
        public string Name { get; } = name;
        public uint Attributes { get; } = attributes;
        public string? FullPath { get; set; }
        public byte[]? SortKey { get; set; }
        public long RankLabel { get; set; }
        public long ParentLabel { get; set; }
        public bool DefaultSystemHeavy { get; set; }
    }

    private sealed record PostingBuilder
    {
        public List<long> Labels { get; } = new(ChunkEntryCount);
    }

    private sealed record ShortTermKey(string Term, FileSearchTextMatch Match);
    private sealed record OrderEntry(long Label, byte[] SortKey);
    private sealed record RankEntry(long Label, long RowId, long ParentLabel, ushort Depth, byte Flags, byte Attributes);
    private sealed record RankChunk(long ChunkId, int EntryCount, byte[] Payload);
    private sealed record OrderChunk(long ChunkId, byte[] FirstSortKey, byte[] LastSortKey, int EntryCount, byte[] Payload);
    private sealed record PostingChunk(long ChunkId, string Term, int MatchClass, byte[] Payload);
    private sealed record ContextInfo(long? CurrentUserLabel, IReadOnlySet<long> SystemRootLabels);

    private sealed class RankMap
    {
        private readonly Dictionary<long, RankEntry> _entries;

        public RankMap(IReadOnlyList<RankEntry> entries)
        {
            _entries = new Dictionary<long, RankEntry>(entries.Count);
            foreach (var entry in entries)
            {
                if (entry.Label <= 0 || !_entries.TryAdd(entry.Label, entry))
                {
                    throw new InvalidOperationException("Short-query rank labels are invalid.");
                }
            }
        }

        public RankEntry Get(long label)
        {
            return _entries.TryGetValue(label, out var entry)
                ? entry
                : throw new InvalidOperationException("Short-query rank label is out of range.");
        }

        public long? FindLabel(long rowId)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.RowId == rowId) return entry.Label;
            }

            return null;
        }
    }
}
