using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public static class NtfsEnumerator
{
    private const uint FsctlEnumUsnData = 0x000900B3;
    private const int BufferSize = 1024 * 1024;
    private const int ErrorHandleEof = 38;

    public static BuildMetrics Enumerate(VolumeDescriptor volume, Action<NamespaceRecord> onRecord)
    {
        using var handle = NtfsVolume.Open(volume.MountPoint);
        return Enumerate(volume, handle, onRecord);
    }

    internal static BuildMetrics Enumerate(
        VolumeDescriptor volume,
        SafeFileHandle handle,
        Action<NamespaceRecord> onRecord)
    {
        var journal = NtfsJournal.Query(handle);
        var input = new MftEnumData(0, 0, journal.NextUsn);
        var buffer = new byte[BufferSize];
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        long records = 0;

        while (true)
        {
            uint returned;
            try
            {
                returned = Ioctl(handle, FsctlEnumUsnData, input, buffer);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorHandleEof)
            {
                break;
            }

            if (returned < sizeof(long))
            {
                throw new InvalidOperationException(
                    "FSCTL_ENUM_USN_DATA returned an invalid buffer.");
            }

            input = input with
            {
                StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0)
            };
            records += ParseRecords(buffer, sizeof(long), checked((int)returned), onRecord);
        }

        stopwatch.Stop();
        return new BuildMetrics(
            records,
            0,
            0,
            stopwatch.Elapsed,
            process.TotalProcessorTime - cpu,
            process.PeakWorkingSet64);
    }

    internal static int ParseRecords(byte[] buffer, int offset, int end, Action<NamespaceRecord> onRecord)
    {
        var records = 0;
        while (offset < end)
        {
            if (end - offset < 8)
            {
                throw new InvalidDataException(
                    "Malformed USN record: fewer than eight bytes remain.");
            }

            var length = BitConverter.ToInt32(buffer, offset);
            var major = BitConverter.ToUInt16(buffer, offset + 4);
            if (length < 8 || length > end - offset)
            {
                throw new InvalidDataException("Malformed USN record length.");
            }

            if (major is not (2 or 3))
            {
                throw new NotSupportedException($"Unsupported USN record major version {major}.");
            }

            var header = major == 2 ? 60 : 76;
            var idLength = major == 2 ? 8 : 16;
            var parentOffset = major == 2 ? 16 : 24;
            var usnOffset = major == 2 ? 24 : 40;
            var attributesOffset = major == 2 ? 52 : 68;
            var nameLengthOffset = major == 2 ? 56 : 72;
            var nameOffsetOffset = major == 2 ? 58 : 74;
            if (length < header)
            {
                throw new InvalidDataException(
                    $"Malformed USN record v{major}: header is incomplete.");
            }

            var nameLength = BitConverter.ToUInt16(buffer, offset + nameLengthOffset);
            var nameOffset = BitConverter.ToUInt16(buffer, offset + nameOffsetOffset);
            if (nameLength % 2 != 0 || nameOffset + nameLength > length)
            {
                throw new InvalidDataException(
                    $"Malformed USN record v{major}: invalid name range.");
            }

            var fileId = new NativeFileId(buffer.AsSpan(offset + 8, idLength));
            var parentFileId = new NativeFileId(buffer.AsSpan(offset + parentOffset, idLength));
            var name = System.Text.Encoding.Unicode.GetString(
                buffer,
                offset + nameOffset,
                nameLength);
            var attributes = BitConverter.ToUInt32(buffer, offset + attributesOffset);
            var usn = BitConverter.ToInt64(buffer, offset + usnOffset);
            onRecord(new NamespaceRecord(fileId, parentFileId, name, attributes, usn, major));

            records++;
            offset += length;
        }

        return records;
    }

    internal static IReadOnlyList<JournalRecord> ParseJournalRecords(byte[] buffer, int offset, int end)
    {
        var result = new List<JournalRecord>();
        while (offset < end)
        {
            if (end - offset < 8)
            {
                throw new InvalidDataException(
                    "Malformed USN record: fewer than eight bytes remain.");
            }

            var length = BitConverter.ToInt32(buffer, offset);
            var major = BitConverter.ToUInt16(buffer, offset + 4);
            if (length < 8 || length > end - offset)
            {
                throw new InvalidDataException("Malformed USN record length.");
            }

            if (major is not (2 or 3))
            {
                throw new NotSupportedException($"Unsupported USN record major version {major}.");
            }

            var header = major == 2 ? 60 : 76;
            var idLength = major == 2 ? 8 : 16;
            var parentOffset = major == 2 ? 16 : 24;
            var usnOffset = major == 2 ? 24 : 40;
            var reasonOffset = major == 2 ? 40 : 56;
            var attributesOffset = major == 2 ? 52 : 68;
            var nameLengthOffset = major == 2 ? 56 : 72;
            var nameOffsetOffset = major == 2 ? 58 : 74;
            if (length < header)
            {
                throw new InvalidDataException(
                    $"Malformed USN record v{major}: header is incomplete.");
            }

            var nameLength = BitConverter.ToUInt16(buffer, offset + nameLengthOffset);
            var nameOffset = BitConverter.ToUInt16(buffer, offset + nameOffsetOffset);
            if (nameLength % 2 != 0 || nameOffset + nameLength > length)
            {
                throw new InvalidDataException(
                    $"Malformed USN record v{major}: invalid name range.");
            }

            var fileId = new NativeFileId(buffer.AsSpan(offset + 8, idLength));
            var parentFileId = new NativeFileId(buffer.AsSpan(offset + parentOffset, idLength));
            var name = System.Text.Encoding.Unicode.GetString(
                buffer,
                offset + nameOffset,
                nameLength);
            var attributes = BitConverter.ToUInt32(buffer, offset + attributesOffset);
            var usn = BitConverter.ToInt64(buffer, offset + usnOffset);
            var record = new NamespaceRecord(fileId, parentFileId, name, attributes, usn, major);
            var reason = BitConverter.ToUInt32(buffer, offset + reasonOffset);
            result.Add(new JournalRecord(record, reason));

            offset += length;
        }

        return result;
    }

    private static uint Ioctl(SafeFileHandle handle, uint code, MftEnumData input, byte[] output)
    {
        var size = Marshal.SizeOf<MftEnumData>();
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
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return returned;
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MftEnumData(
        ulong StartFileReferenceNumber,
        long LowUsn,
        long HighUsn);

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
