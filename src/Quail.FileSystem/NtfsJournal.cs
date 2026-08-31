using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public static class NtfsJournal
{
    private const uint FsctlQueryUsnJournal = 0x000900F4;
    private const uint FsctlReadUsnJournal = 0x000900BB;
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
        return Read(handle, checkpoint, onBatch);
    }

    internal static long Read(SafeFileHandle handle, IncrementalCheckpoint checkpoint, Action<JournalBatch> onBatch)
    {
        var input = new ReadUsnJournalDataV1(checkpoint.NextUsn, uint.MaxValue, 0, 0, 0, checkpoint.JournalId, 2, 3);
        var buffer = new byte[BufferSize];
        var cursor = checkpoint.NextUsn;

        while (true)
        {
            var returned = Ioctl(handle, FsctlReadUsnJournal, input, buffer);
            if (returned < sizeof(long))
            {
                throw new InvalidDataException(
                    "FSCTL_READ_USN_JOURNAL returned an invalid buffer.");
            }

            var nextUsn = BitConverter.ToInt64(buffer, 0);
            if (nextUsn < cursor)
            {
                throw new InvalidDataException(
                    "FSCTL_READ_USN_JOURNAL moved the cursor backwards.");
            }

            var records = NtfsEnumerator.ParseJournalRecords(buffer, sizeof(long), checked((int)returned));
            if (records.Count > 0)
            {
                onBatch(new JournalBatch(nextUsn, records));
            }
            cursor = nextUsn;
            if (records.Count == 0 || nextUsn == input.StartUsn) return cursor;
            input = input with { StartUsn = nextUsn };
        }
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
    private readonly record struct ReadUsnJournalDataV1(
        long StartUsn,
        uint ReasonMask,
        uint ReturnOnlyOnClose,
        ulong Timeout,
        ulong BytesToWait,
        ulong UsnJournalId,
        ushort MinMajorVersion,
        ushort MaxMajorVersion);

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
