using Quail.FileSystem;

namespace Quail.Core.Tests;

public sealed class NtfsJournalBoundedReadTests
{
    [Fact]
    public void Legacy_unbounded_reader_self_chases_records_generated_by_batch_application()
    {
        var source = new SelfExtendingJournalSource(initialCursor: 100, firstNextUsn: 120);
        var cursor = 100L;

        for (var batch = 0; batch < 8; batch++)
        {
            var page = source.Read(cursor);
            Assert.NotEmpty(page.Records);
            source.GenerateWriteAfter(page.NextUsn);
            cursor = page.NextUsn;
        }

        Assert.Equal(8, source.ReadCount);
        Assert.True(source.HasPage(cursor));
    }

    [Fact]
    public void Captured_frontier_stops_self_chasing_records_generated_by_batch_application()
    {
        var source = new SelfExtendingJournalSource(initialCursor: 100, firstNextUsn: 120);
        var appliedUsns = new List<long>();

        var cursor = NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 120,
            source.Read,
            batch =>
            {
                appliedUsns.AddRange(batch.Records.Select(record => record.NamespaceRecord.Usn));
                source.GenerateWriteAfter(batch.NextUsn);
            });

        Assert.Equal(120, cursor);
        Assert.Equal([110], appliedUsns);
        Assert.Equal(1, source.ReadCount);
        Assert.True(source.HasPage(120));
    }

    [Fact]
    public void Page_crossing_frontier_applies_only_half_open_range_and_returns_exact_frontier()
    {
        var applied = new List<JournalBatch>();

        var cursor = NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 120,
            startUsn => startUsn == 100
                ? Page(100, 160, 110, 119, 120, 140)
                : Page(startUsn, startUsn),
            applied.Add);

        var batch = Assert.Single(applied);
        Assert.Equal(120, cursor);
        Assert.Equal(120, batch.NextUsn);
        Assert.Equal([110, 119], batch.Records.Select(record => record.NamespaceRecord.Usn));
    }

    [Fact]
    public void External_change_during_sync_is_replayed_from_durable_frontier_by_next_iteration()
    {
        var firstApplied = new List<long>();
        var secondApplied = new List<long>();

        NtfsJournal.JournalReadPage Read(long startUsn) => startUsn switch
        {
            100 => Page(100, 150, 110, 125),
            120 => Page(120, 150, 125),
            _ => Page(startUsn, startUsn)
        };

        var firstCursor = NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 120,
            Read,
            batch => firstApplied.AddRange(batch.Records.Select(record => record.NamespaceRecord.Usn)));

        var secondCursor = NtfsJournal.ReadPages(
            Checkpoint(firstCursor),
            exclusiveUpperBound: 150,
            Read,
            batch => secondApplied.AddRange(batch.Records.Select(record => record.NamespaceRecord.Usn)));

        Assert.Equal(120, firstCursor);
        Assert.Equal([110], firstApplied);
        Assert.Equal(150, secondCursor);
        Assert.Equal([125], secondApplied);
    }

    [Fact]
    public void Empty_captured_range_performs_no_native_read()
    {
        var readCount = 0;

        var cursor = NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 100,
            startUsn =>
            {
                readCount++;
                return Page(startUsn, startUsn);
            },
            _ => throw new InvalidOperationException("An empty range must not apply a batch."));

        Assert.Equal(100, cursor);
        Assert.Equal(0, readCount);
    }

    [Fact]
    public void Split_pages_commit_only_proven_cursors_and_clip_the_crossing_page()
    {
        var batches = new List<JournalBatch>();

        var cursor = NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 120,
            startUsn => startUsn switch
            {
                100 => Page(100, 110, 105),
                110 => Page(110, 150, 115, 120, 140),
                _ => Page(startUsn, startUsn)
            },
            batches.Add);

        Assert.Equal(120, cursor);
        Assert.Equal([110, 120], batches.Select(batch => batch.NextUsn));
        Assert.Equal(
            [105, 115],
            batches.SelectMany(batch => batch.Records).Select(record => record.NamespaceRecord.Usn));
    }

    [Fact]
    public void Failed_batch_application_does_not_advance_the_uncommitted_cursor()
    {
        var durableCursor = 100L;

        Assert.Throws<IOException>(() => NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 130,
            startUsn => startUsn switch
            {
                100 => Page(100, 110, 105),
                110 => Page(110, 130, 115),
                _ => Page(startUsn, startUsn)
            },
            batch =>
            {
                if (batch.NextUsn == 130)
                {
                    throw new IOException("Injected batch failure before commit.");
                }

                durableCursor = batch.NextUsn;
            }));

        Assert.Equal(110, durableCursor);
    }

    [Fact]
    public void Captured_frontier_before_checkpoint_fails_closed_without_reading()
    {
        var readCount = 0;

        Assert.Throws<InvalidDataException>(() => NtfsJournal.ReadPages(
            Checkpoint(100),
            exclusiveUpperBound: 99,
            startUsn =>
            {
                readCount++;
                return Page(startUsn, startUsn);
            },
            _ => { }));

        Assert.Equal(0, readCount);
    }

    private static IncrementalCheckpoint Checkpoint(long nextUsn) => new(1, nextUsn, 1, 1);

    private static NtfsJournal.JournalReadPage Page(long startUsn, long nextUsn, params long[] recordUsns) =>
        new(startUsn, nextUsn, recordUsns.Select(Record).ToArray());

    private static JournalRecord Record(long usn) => new(
        new NamespaceRecord(
            new NativeFileId(BitConverter.GetBytes(usn)),
            new NativeFileId(new byte[8]),
            $"entry-{usn}",
            0,
            usn,
            2),
        UsnReason.FileCreate);

    private sealed class SelfExtendingJournalSource(long initialCursor, long firstNextUsn)
    {
        private readonly Dictionary<long, NtfsJournal.JournalReadPage> _pages = new()
        {
            [initialCursor] = Page(initialCursor, firstNextUsn, firstNextUsn - 10)
        };

        public int ReadCount { get; private set; }

        public NtfsJournal.JournalReadPage Read(long startUsn)
        {
            ReadCount++;
            if (ReadCount > 32)
            {
                throw new InvalidOperationException("Deterministic self-chasing guard reached.");
            }

            return _pages.TryGetValue(startUsn, out var page)
                ? page
                : Page(startUsn, startUsn);
        }

        public void GenerateWriteAfter(long cursor)
        {
            var nextUsn = checked(cursor + 10);
            _pages[cursor] = Page(cursor, nextUsn, cursor + 1);
        }

        public bool HasPage(long cursor) => _pages.ContainsKey(cursor);
    }
}
