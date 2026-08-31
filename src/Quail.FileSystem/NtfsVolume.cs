using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public static class NtfsVolume
{
    public static VolumeDescriptor Validate(string mountPoint)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(mountPoint))
            ?? throw new ArgumentException("A drive-rooted path is required.", nameof(mountPoint));
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"'{root}' is not a ready local NTFS volume.");
        }

        var volumeName = new System.Text.StringBuilder(256);
        if (!GetVolumeNameForVolumeMountPoint(root, volumeName, volumeName.Capacity))
        {
            ThrowLastError("GetVolumeNameForVolumeMountPoint");
        }
        return new VolumeDescriptor(volumeName.ToString().TrimEnd('\\'), root, drive.DriveFormat, drive.VolumeLabel);
    }

    internal static SafeFileHandle Open(string mountPoint)
    {
        var letter = char.ToUpperInvariant(mountPoint[0]) + ":";
        var handle = CreateFile(
            $@"\\.\{letter}",
            0x80000000,
            0x00000001 | 0x00000002 | 0x00000004,
            IntPtr.Zero,
            3,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            ThrowLastError($"CreateFile({letter})");
        }
        return handle;
    }

    internal static IReadOnlyList<NamespaceRecord> GetRootRecords(VolumeDescriptor volume)
    {
        var handle = CreateFile(
            volume.MountPoint,
            0,
            0x00000001 | 0x00000002 | 0x00000004,
            IntPtr.Zero,
            3,
            0x02000000,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            ThrowLastError($"CreateFile({volume.MountPoint})");
        }
        using (handle)
        {
            if (!GetFileInformationByHandle(handle, out var information))
            {
                ThrowLastError("GetFileInformationByHandle");
            }
            var value = ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow;
            var legacyId = new NativeFileId(BitConverter.GetBytes(value));
            var fileIdInfo = new byte[24];
            if (!GetFileInformationByHandleEx(handle, 18, fileIdInfo, fileIdInfo.Length))
            {
                ThrowLastError("GetFileInformationByHandleEx(FileIdInfo)");
            }
            var extendedId = new NativeFileId(fileIdInfo.AsSpan(8, 16));
            return new[]
            {
                new NamespaceRecord(legacyId, legacyId, string.Empty, information.FileAttributes, 0, 2),
                new NamespaceRecord(extendedId, extendedId, string.Empty, information.FileAttributes, 0, 3),
            };
        }
    }

    private static void ThrowLastError(string operation) =>
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(
        string mountPoint,
        System.Text.StringBuilder volumeName,
        int bufferLength);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string name,
        uint access,
        uint share,
        IntPtr security,
        uint creation,
        uint flags,
        IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(
        SafeFileHandle handle,
        out ByHandleFileInformation information);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(
        SafeFileHandle handle,
        int informationClass,
        [Out] byte[] information,
        int size);
    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
