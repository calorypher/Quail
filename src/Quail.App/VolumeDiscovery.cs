using Quail.FileSystem;

namespace Quail.App;

internal sealed record DiscoveredVolume(string VolumeIdentity, string MountPoint, string Label);

internal static class VolumeDiscovery
{
    public static IReadOnlyList<DiscoveredVolume> Discover()
    {
        var discovered = new List<DiscoveredVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed || !string.Equals(drive.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) continue;
                var volume = NtfsVolume.Validate(drive.RootDirectory.FullName);
                discovered.Add(new(volume.StableIdentity, volume.MountPoint, volume.Label));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                AppLog.Write($"Volume discovery skipped '{drive.Name}'.", exception);
            }
        }
        return discovered.GroupBy(volume => volume.VolumeIdentity, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).OrderBy(volume => volume.MountPoint, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
