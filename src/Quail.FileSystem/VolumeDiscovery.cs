namespace Quail.FileSystem;

public static class VolumeDiscovery
{
    public static IReadOnlyList<VolumeDescriptor> Discover()
    {
        var discovered = new List<VolumeDescriptor>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) continue;
                discovered.Add(NtfsVolume.Validate(drive.RootDirectory.FullName));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
            }
        }
        return discovered.GroupBy(volume => volume.StableIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
