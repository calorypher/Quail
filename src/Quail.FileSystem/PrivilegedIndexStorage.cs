using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public sealed class PrivilegedIndexStorageLease : IDisposable
{
    private readonly IReadOnlyList<IDisposable> _resources;

    internal PrivilegedIndexStorageLease(string databasePath, IReadOnlyList<IDisposable> resources)
    {
        DatabasePath = databasePath;
        _resources = resources;
    }

    public string DatabasePath { get; }

    public void Dispose()
    {
        for (var index = _resources.Count - 1; index >= 0; index--)
        {
            _resources[index].Dispose();
        }
    }
}

public static class PrivilegedIndexStorage
{
    private const string SecureDirectorySddl = "O:BAG:SYD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;GRGX;;;BU)";
    private const uint FileListDirectory = 0x0001;
    private const uint FileReadAttributes = 0x0080;
    private const uint ReadControl = 0x00020000;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const int ErrorAlreadyExists = 183;
    private const int FileAttributeTagInfoClass = 9;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier System = new(WellKnownSidType.LocalSystemSid, null);
    private const FileSystemRights DangerousRights =
        FileSystemRights.WriteData |
        FileSystemRights.AppendData |
        FileSystemRights.WriteExtendedAttributes |
        FileSystemRights.WriteAttributes |
        FileSystemRights.Delete |
        FileSystemRights.DeleteSubdirectoriesAndFiles |
        FileSystemRights.ChangePermissions |
        FileSystemRights.TakeOwnership;

    public static string RootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Quail");

    public static string IndexesPath => Path.Combine(RootPath, "Indexes");

    public static PrivilegedIndexStorageLease Acquire(string volumeIdentity)
    {
        var resources = new List<IDisposable>();
        try
        {
            var commonApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            resources.Add(OpenDirectoryWithoutReparse(commonApplicationData));
            resources.Add(OpenOrCreateSecureDirectory(RootPath));
            resources.Add(OpenOrCreateSecureDirectory(IndexesPath));
            var locksPath = Path.Combine(RootPath, "Locks");
            resources.Add(OpenOrCreateSecureDirectory(locksPath));

            var databasePath = ManagedIndexPath.ForVolumeIdentity(volumeIdentity);
            ValidateWritableFiles(databasePath);

            var lockPath = Path.Combine(locksPath, $"{ManagedIndexPath.SafeVolumeName(volumeIdentity)}.lock");
            ValidateNotReparseIfPresent(lockPath);
            try
            {
                resources.Add(new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
            }
            catch (IOException exception)
            {
                throw new InvalidOperationException("Another index operation is already running for this volume.", exception);
            }

            return new PrivilegedIndexStorageLease(databasePath, resources);
        }
        catch
        {
            for (var index = resources.Count - 1; index >= 0; index--)
            {
                resources[index].Dispose();
            }
            throw;
        }
    }

    internal static void ValidateWritableFiles(string databasePath)
    {
        foreach (var path in new[]
        {
            databasePath,
            databasePath + "-journal",
            databasePath + "-wal",
            databasePath + "-shm",
            databasePath + ".building",
            databasePath + ".building-journal",
            databasePath + ".building-wal",
            databasePath + ".building-shm",
            databasePath + ".previous"
        })
        {
            ValidateNotReparseIfPresent(path);
        }
    }

    internal static void ValidateNotReparseIfPresent(string path)
    {
        using var handle = CreateFileW(
            path,
            FileReadAttributes,
            ShareRead | ShareWrite,
            0,
            OpenExisting,
            FileFlagOpenReparsePoint | FileFlagBackupSemantics,
            0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error is 2 or 3)
            {
                return;
            }

            throw new Win32Exception(error, $"Could not validate protected storage object '{path}'.");
        }

        EnsureHandleIsNotReparse(handle, path);
    }

    private static SafeFileHandle OpenOrCreateSecureDirectory(string path)
    {
        var created = CreateDirectoryWithSecureAcl(path);
        var handle = OpenDirectoryWithoutReparse(path);
        try
        {
            ValidateSecureDirectoryAcl(path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            if (created)
            {
                try { Directory.Delete(path); } catch { }
            }
            throw;
        }
    }

    private static bool CreateDirectoryWithSecureAcl(string path)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(SecureDirectorySddl, 1, out var descriptor, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the protected-storage security descriptor.");
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false
            };
            if (CreateDirectoryW(path, ref attributes))
            {
                return true;
            }

            var error = Marshal.GetLastWin32Error();
            if (error == ErrorAlreadyExists)
            {
                return false;
            }

            throw new Win32Exception(error, $"Could not create protected storage directory '{path}'.");
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    private static SafeFileHandle OpenDirectoryWithoutReparse(string path)
    {
        var handle = CreateFileW(
            path,
            FileListDirectory | FileReadAttributes | ReadControl,
            ShareRead | ShareWrite,
            0,
            OpenExisting,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            0);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open protected storage directory '{path}'.");
        }

        try
        {
            EnsureHandleIsNotReparse(handle, path);
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void EnsureHandleIsNotReparse(SafeFileHandle handle, string path)
    {
        if (!GetFileInformationByHandleEx(handle, FileAttributeTagInfoClass, out var information, (uint)Marshal.SizeOf<FileAttributeTagInfo>()))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not inspect protected storage object '{path}'.");
        }

        if ((information.FileAttributes & FileAttributeReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Protected storage contains a reparse point: '{path}'.");
        }
    }

    private static void ValidateSecureDirectoryAcl(string path)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier
            ?? throw new UnauthorizedAccessException($"Protected storage has no trusted owner: '{path}'.");
        if (!owner.Equals(Administrators) && !owner.Equals(System))
        {
            throw new UnauthorizedAccessException($"Protected storage has an untrusted owner: '{path}'.");
        }

        if (!security.AreAccessRulesProtected)
        {
            throw new UnauthorizedAccessException($"Protected storage inherits an unsafe access policy: '{path}'.");
        }

        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, true, typeof(SecurityIdentifier)))
        {
            var identity = rule.IdentityReference as SecurityIdentifier
                ?? throw new UnauthorizedAccessException($"Protected storage contains an unknown access identity: '{path}'.");
            if (rule.AccessControlType == AccessControlType.Allow &&
                !identity.Equals(Administrators) &&
                !identity.Equals(System) &&
                GrantsDangerousRights(rule.FileSystemRights))
            {
                throw new UnauthorizedAccessException($"Protected storage grants write access outside Administrators and SYSTEM: '{path}'.");
            }
        }
    }

    public static bool GrantsDangerousRights(FileSystemRights rights) => (rights & DangerousRights) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileAttributeTagInfo
    {
        public uint FileAttributes;
        public uint ReparseTag;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateDirectoryW(string path, ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileAttributeTagInfo information, uint bufferSize);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string stringSecurityDescriptor, uint stringSdRevision, out nint securityDescriptor, out uint securityDescriptorSize);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
