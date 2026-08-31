namespace Quail.FileSystem;

public enum FileSystemIndexOperation
{
    Build,
    Rebuild,
    Refresh
}

public enum FileSystemIndexOperationOutcome
{
    Succeeded,
    RebuildRequired,
    VolumeRejected,
    CatalogRejected,
    StorageRejected,
    IndexOperationFailed
}

public sealed record FileSystemIndexOperationRequest(
    FileSystemIndexOperation Operation,
    string MountPoint,
    string VolumeIdentity);

public sealed record FileSystemIndexPresentation(
    IndexStatus Status,
    string? VolumeHeadline,
    string? VolumeDetail);

public static class FileSystemIndexAdministration
{
    public static FileSystemIndexOperationOutcome Run(FileSystemIndexOperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        VolumeDescriptor volume;
        try
        {
            volume = NtfsVolume.Validate(request.MountPoint);
            if (!string.Equals(volume.StableIdentity, request.VolumeIdentity, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The configured mount point now refers to a different volume.");
        }
        catch
        {
            return FileSystemIndexOperationOutcome.VolumeRejected;
        }

        string path;
        try
        {
            var catalog = new IndexCatalogStore().LoadAsync().GetAwaiter().GetResult();
            path = ManagedIndexPath.ForVolumeIdentity(volume.StableIdentity);
            if (!catalog.IsValid || !catalog.Catalog.Entries.Any(entry =>
                    string.Equals(entry.VolumeIdentity, request.VolumeIdentity, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.MountPoint, volume.MountPoint, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.DatabasePath, path, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The requested volume is not configured for Quail.");
        }
        catch
        {
            return FileSystemIndexOperationOutcome.CatalogRejected;
        }

        PrivilegedIndexStorageLease storage;
        try
        {
            storage = PrivilegedIndexStorage.Acquire(volume.StableIdentity);
            if (!string.Equals(storage.DatabasePath, path, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Protected index storage resolved an unexpected database path.");
        }
        catch
        {
            return FileSystemIndexOperationOutcome.StorageRejected;
        }

        using (storage)
        try
        {
            var store = new IndexStore(path, IndexStoreJournalLifecycle.DeleteWhenQuiescent);
            if (request.Operation is FileSystemIndexOperation.Build or FileSystemIndexOperation.Rebuild)
            {
                store.Build(volume.MountPoint);
                return FileSystemIndexOperationOutcome.Succeeded;
            }

            var sync = store.Sync(volume.MountPoint);
            return sync.RebuildRequired
                ? FileSystemIndexOperationOutcome.RebuildRequired
                : FileSystemIndexOperationOutcome.Succeeded;
        }
        catch
        {
            return FileSystemIndexOperationOutcome.IndexOperationFailed;
        }
    }

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
