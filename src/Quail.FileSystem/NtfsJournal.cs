using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public static class NtfsJournal
{
    internal const uint FsctlQueryUsnJournal = 0x000900F4;
    internal const uint FsctlReadUsnJournal = 0x000900BB;
    private const int BufferSize = 1024 * 1024;

    public static UsnJournalState Query(VolumeDescriptor volume)
    {
        using var handle = NtfsVolume.Open(volume.MountPoint);
        return Query(handle);
    }

    internal static UsnJournalState Query(SafeFileHandle handle)
    {
        var output = new byte[64];
        if (!DeviceIoControl(
                handle,
                FsctlQueryUsnJournal,
                IntPtr.Zero,
                0,
                output,
                (uint)output.Length,
                out var returned,
                IntPtr.Zero) || returned < 56)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "FSCTL_QUERY_USN_JOURNAL");
        }

        return new UsnJournalState(
            BitConverter.ToUInt64(output, 0),
            BitConverter.ToInt64(output, 8),
            BitConverter.ToInt64(output, 16),
            BitConverter.ToInt64(output, 24),
            returned >= 60 ? BitConverter.ToUInt16(output, 56) : (ushort)2,
            returned >= 62 ? BitConverter.ToUInt16(output, 58) : (ushort)3);
    }

    public static long Read(VolumeDescriptor volume, IncrementalCheckpoint checkpoint, Action<JournalBatch> onBatch)
    {
        using var handle = NtfsVolume.Open(volume.MountPoint);
        var journal = Query(handle);
        return Read(handle, checkpoint, journal.NextUsn, onBatch);
    }

    internal static long Read(
        VolumeDescriptor volume,
        IncrementalCheckpoint checkpoint,
        long exclusiveUpperBound,
        Action<JournalBatch> onBatch)
    {
        using var handle = NtfsVolume.Open(volume.MountPoint);
        return Read(handle, checkpoint, exclusiveUpperBound, onBatch);
    }

    /// <summary>
    /// Waits for a possible USN journal change. Completion is only a change signal;
    /// callers must query the journal and run authoritative catch-up afterwards.
    /// </summary>
    public static async Task WaitForChangesAsync(
        VolumeDescriptor volume,
        IncrementalCheckpoint checkpoint,
        CancellationToken cancellationToken = default)
    {
        using var handle = NtfsVolume.Open(volume.MountPoint, overlapped: true);
        await UsnJournalWait.WaitAsync(handle, checkpoint, cancellationToken).ConfigureAwait(false);
    }

    internal static long Read(SafeFileHandle handle, IncrementalCheckpoint checkpoint, Action<JournalBatch> onBatch)
    {
        var journal = Query(handle);
        return Read(handle, checkpoint, journal.NextUsn, onBatch);
    }

    internal static long Read(
        SafeFileHandle handle,
        IncrementalCheckpoint checkpoint,
        long exclusiveUpperBound,
        Action<JournalBatch> onBatch)
    {
        var buffer = new byte[BufferSize];
        return ReadPages(
            checkpoint,
            exclusiveUpperBound,
            cursor => ReadPage(handle, checkpoint.JournalId, cursor, buffer),
            onBatch);
    }

    internal static long ReadPages(
        IncrementalCheckpoint checkpoint,
        long exclusiveUpperBound,
        Func<long, JournalReadPage> readPage,
        Action<JournalBatch> onBatch)
    {
        if (exclusiveUpperBound < checkpoint.NextUsn)
        {
            throw new InvalidDataException(
                "The captured USN journal frontier precedes the durable checkpoint.");
        }

        var cursor = checkpoint.NextUsn;
        if (cursor == exclusiveUpperBound)
        {
            return cursor;
        }

        while (cursor < exclusiveUpperBound)
        {
            var page = readPage(cursor);
            if (page.StartUsn != cursor)
            {
                throw new InvalidDataException(
                    "FSCTL_READ_USN_JOURNAL returned a page for an unexpected cursor.");
            }

            var nextUsn = page.NextUsn;
            if (nextUsn < cursor)
            {
                throw new InvalidDataException(
                    "FSCTL_READ_USN_JOURNAL moved the cursor backwards.");
            }

            long previousRecordUsn = cursor;
            foreach (var record in page.Records)
            {
                var recordUsn = record.NamespaceRecord.Usn;
                if (recordUsn < cursor || recordUsn < previousRecordUsn || recordUsn >= nextUsn)
                {
                    throw new InvalidDataException(
                        "FSCTL_READ_USN_JOURNAL returned inconsistent record ordering.");
                }

                previousRecordUsn = recordUsn;
            }

            // QUERY_USN_JOURNAL.NextUsn is the first USN outside this invocation's
            // half-open range. A live read may return a buffer that crosses it;
            // later records must remain available from the durable frontier to the
            // next authoritative Sync instead of extending this read indefinitely.
            var boundedNextUsn = Math.Min(nextUsn, exclusiveUpperBound);
            IReadOnlyList<JournalRecord> boundedRecords = page.Records;
            if (nextUsn > exclusiveUpperBound)
            {
                boundedRecords = page.Records
                    .TakeWhile(record => record.NamespaceRecord.Usn < exclusiveUpperBound)
                    .ToArray();
            }

            if (boundedRecords.Count > 0)
            {
                onBatch(new JournalBatch(boundedNextUsn, boundedRecords));
            }

            if (nextUsn >= exclusiveUpperBound)
            {
                return exclusiveUpperBound;
            }

            if (nextUsn == cursor)
            {
                throw new InvalidDataException(
                    "FSCTL_READ_USN_JOURNAL did not reach the captured frontier.");
            }

            cursor = nextUsn;
        }

        return cursor;
    }

    private static JournalReadPage ReadPage(
        SafeFileHandle handle,
        ulong journalId,
        long startUsn,
        byte[] buffer)
    {
        var input = new ReadUsnJournalDataV1(startUsn, uint.MaxValue, 0, 0, 0, journalId, 2, 3);
        var returned = Ioctl(handle, FsctlReadUsnJournal, input, buffer);
        if (returned < sizeof(long))
        {
            throw new InvalidDataException(
                "FSCTL_READ_USN_JOURNAL returned an invalid buffer.");
        }

        var nextUsn = BitConverter.ToInt64(buffer, 0);
        var records = NtfsEnumerator.ParseJournalRecords(buffer, sizeof(long), checked((int)returned));
        return new JournalReadPage(startUsn, nextUsn, records);
    }

    private static uint Ioctl(SafeFileHandle handle, uint code, ReadUsnJournalDataV1 input, byte[] output)
    {
        var size = Marshal.SizeOf<ReadUsnJournalDataV1>();
        var pointer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(input, pointer, false);
            if (!DeviceIoControl(
                    handle,
                    code,
                    pointer,
                    (uint)size,
                    output,
                    (uint)output.Length,
                    out var returned,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "FSCTL_READ_USN_JOURNAL");
            }

            return returned;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct ReadUsnJournalDataV1(
        long StartUsn,
        uint ReasonMask,
        uint ReturnOnlyOnClose,
        ulong Timeout,
        ulong BytesToWait,
        ulong UsnJournalId,
        ushort MinMajorVersion,
        ushort MaxMajorVersion);

    internal readonly record struct JournalReadPage(
        long StartUsn,
        long NextUsn,
        IReadOnlyList<JournalRecord> Records);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle handle,
        uint code,
        IntPtr input,
        uint inputSize,
        [Out] byte[] output,
        uint outputSize,
        out uint returned,
        IntPtr overlapped);
}
