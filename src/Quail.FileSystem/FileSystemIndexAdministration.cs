namespace Quail.FileSystem;

public sealed record FileSystemIndexPresentation(
    IndexStatus Status,
    string? VolumeHeadline,
    string? VolumeDetail);

public static class FileSystemIndexAdministration
{
    public static IndexStatus GetStatus(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        return new IndexStore(databasePath).GetStatus();
    }

    public static FileSystemIndexPresentation GetPresentation(IndexCatalogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var status = GetStatus(entry.DatabasePath);
        try
        {
            var current = NtfsVolume.Validate(entry.MountPoint);
            return string.Equals(current.StableIdentity, entry.VolumeIdentity, StringComparison.OrdinalIgnoreCase)
                ? new(status, null, null)
                : new(status, "Volume mismatch", "The mounted volume no longer matches this configuration. Reconfigure this entry.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new(status, "Volume unavailable", "The configured volume is unavailable or could not be validated.");
        }
    }
}
