using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

internal sealed class NtfsMetadataAcquirer : IDisposable
{
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private static readonly byte[] ZeroHighFileId = new byte[8];
    private readonly SafeFileHandle _volumeHandle;
    private readonly Dictionary<int, long> _failureCodes = new();
    private long _attempts;
    private long _succeeded;
    private long _failed;

    public NtfsMetadataAcquirer(VolumeDescriptor volume) => _volumeHandle = NtfsVolume.Open(volume.MountPoint);

    internal SafeFileHandle VolumeHandle => _volumeHandle;

    public MetadataAcquisitionMetrics Metrics => new(
        _attempts,
        _succeeded,
        _failed,
        _failureCodes.Count == 0 ? "none" : string.Join(',', _failureCodes.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")));

    public FileMetadata Acquire(NamespaceRecord record)
    {
        _attempts++;
        var failed = false;
        uint attributes = record.Attributes;
        long? lastWriteTime = null;
        long? logicalSize = null;

        try
        {
            using var handle = OpenById(record.FileId);
            try
            {
                var basic = GetBasic(handle);
                attributes = basic.FileAttributes;
                if (basic.LastWriteTime > 0) lastWriteTime = basic.LastWriteTime;
            }
            catch (Win32Exception exception)
            {
                failed = true;
                RecordFailure(exception.NativeErrorCode);
            }

            try
            {
                var standard = GetStandard(handle);
                var isDirectory = standard.Directory || (attributes & FileAttributeDirectory) != 0;
                var isReparse = (attributes & FileAttributeReparsePoint) != 0;
                if (!isDirectory && !isReparse) logicalSize = standard.EndOfFile;
            }
            catch (Win32Exception exception)
            {
                failed = true;
                RecordFailure(exception.NativeErrorCode);
            }
        }
        catch (Win32Exception exception)
        {
            failed = true;
            RecordFailure(exception.NativeErrorCode);
        }

        if (failed)
        {
            _failed++;
        }
        else
        {
            _succeeded++;
        }
        return new FileMetadata(logicalSize, lastWriteTime);
    }

    public void Dispose() => _volumeHandle.Dispose();

    private SafeFileHandle OpenById(NativeFileId fileId)
    {
        var bytes = fileId.Bytes.Span;
        if (bytes.Length != 16 || !bytes[8..].SequenceEqual(ZeroHighFileId))
            throw new NotSupportedException("Metadata acquisition requires the validated canonical NTFS file identifier shape.");
        var descriptor = new FileIdDescriptor
        {
            Size = checked((uint)Marshal.SizeOf<FileIdDescriptor>()),
            Type = FileIdType.FileId,
            FileId = new FileId128 { Low = BitConverter.ToUInt64(bytes[..8]) },
        };
        var handle = OpenFileById(_volumeHandle, in descriptor, 0, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero, FileFlagBackupSemantics | FileFlagOpenReparsePoint);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenFileById");
        return handle;
    }

    private static FileBasicInfo GetBasic(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, FileInfoClass.FileBasicInfo, out FileBasicInfo information, checked((uint)Marshal.SizeOf<FileBasicInfo>())))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandleEx(FileBasicInfo)");
        return information;
    }

    private static FileStandardInfo GetStandard(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, FileInfoClass.FileStandardInfo, out FileStandardInfo information, checked((uint)Marshal.SizeOf<FileStandardInfo>())))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFileInformationByHandleEx(FileStandardInfo)");
        return information;
    }

    private void RecordFailure(int code) => _failureCodes[code] = _failureCodes.GetValueOrDefault(code) + 1;

    private enum FileIdType : uint
    {
        FileId = 0
    }

    private enum FileInfoClass : int
    {
        FileBasicInfo = 0,
        FileStandardInfo = 1
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileId128
    {
        public ulong Low;
        public ulong High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdDescriptor
    {
        public uint Size;
        public FileIdType Type;
        public FileId128 FileId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileBasicInfo
    {
        public long CreationTime;
        public long LastAccessTime;
        public long LastWriteTime;
        public long ChangeTime;
        public uint FileAttributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileStandardInfo
    {
        public long AllocationSize;
        public long EndOfFile;
        public uint NumberOfLinks;

        [MarshalAs(UnmanagedType.U1)]
        public bool DeletePending;

        [MarshalAs(UnmanagedType.U1)]
        public bool Directory;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle OpenFileById(SafeFileHandle volumeHint, in FileIdDescriptor fileId, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint flagsAndAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, FileInfoClass informationClass, out FileBasicInfo information, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, FileInfoClass informationClass, out FileStandardInfo information, uint size);
}
