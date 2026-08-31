using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quail.FileSystem;

public sealed record IndexCatalogDocument(int Version, IReadOnlyList<IndexCatalogEntry> Entries)
{
    public const int CurrentVersion = 1;
    public static readonly IndexCatalogDocument Empty = new(CurrentVersion, []);
}

public sealed record IndexCatalogEntry(string VolumeIdentity, string MountPoint, string DatabasePath, bool EnabledForSearch);

public sealed record IndexCatalogLoadResult(IndexCatalogDocument Catalog, string? Error)
{
    public bool IsValid => Error is null;
}

public interface IIndexCatalogStore
{
    Task<IndexCatalogLoadResult> LoadAsync();
    Task SaveAsync(IndexCatalogDocument catalog);
}

public sealed class IndexCatalogStore : IIndexCatalogStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public IndexCatalogStore() : this(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quail", "indexes.json")) { }
    public IndexCatalogStore(string path) => _path = path;
    public string Path => _path;

    public async Task<IndexCatalogLoadResult> LoadAsync()
    {
        if (!File.Exists(_path)) return new(IndexCatalogDocument.Empty, null);
        try
        {
            await using var stream = File.OpenRead(_path);
            var catalog = await JsonSerializer.DeserializeAsync<IndexCatalogDocument>(stream, SerializerOptions);
            if (catalog is null || catalog.Version != IndexCatalogDocument.CurrentVersion)
                throw new InvalidDataException("Index catalog has an unsupported format.");
            var entries = catalog.Entries ?? [];
            if (entries.Any(entry => !TryNormalizeEntry(entry, out _)) || entries.GroupBy(entry => entry.VolumeIdentity, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
                throw new InvalidDataException("Index catalog contains invalid or duplicate volume entries.");
            return new(new IndexCatalogDocument(catalog.Version, entries.Select(entry => { TryNormalizeEntry(entry, out var normalized); return normalized!; }).ToArray()), null);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            return new(IndexCatalogDocument.Empty, "Index configuration could not be read.");
        }
    }

    public async Task SaveAsync(IndexCatalogDocument catalog)
    {
        if (catalog.Version != IndexCatalogDocument.CurrentVersion || catalog.Entries.Any(entry => !IsValidEntry(entry)) ||
            catalog.Entries.GroupBy(entry => entry.VolumeIdentity, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() != 1))
            throw new InvalidDataException("Refusing to save an invalid index catalog.");
        var directory = System.IO.Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = System.IO.Path.Combine(directory, $"indexes-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, catalog, SerializerOptions);
                await stream.FlushAsync();
            }
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private static bool IsValidEntry(IndexCatalogEntry entry)
    {
        return TryNormalizeEntry(entry, out var normalized) && string.Equals(entry.DatabasePath, normalized!.DatabasePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormalizeEntry(IndexCatalogEntry entry, out IndexCatalogEntry? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(entry.VolumeIdentity) || string.IsNullOrWhiteSpace(entry.MountPoint) || string.IsNullOrWhiteSpace(entry.DatabasePath))
        {
            return false;
        }

        try
        {
            var actual = System.IO.Path.GetFullPath(entry.DatabasePath);
            var current = System.IO.Path.GetFullPath(ManagedIndexPath.ForVolumeIdentity(entry.VolumeIdentity));
            var legacy = System.IO.Path.GetFullPath(ManagedIndexPath.LegacyForVolumeIdentity(entry.VolumeIdentity));
            if (!string.Equals(actual, current, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actual, legacy, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalized = entry with { DatabasePath = current };
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}

public static class ManagedIndexPath
{
    public static string ForVolumeIdentity(string volumeIdentity)
        => System.IO.Path.Combine(PrivilegedIndexStorage.IndexesPath, $"{SafeVolumeName(volumeIdentity)}.db");

    public static string LegacyForVolumeIdentity(string volumeIdentity)
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quail", "Indexes", $"{SafeVolumeName(volumeIdentity)}.db");

    public static string SafeVolumeName(string volumeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        var normalizedIdentity = volumeIdentity.TrimEnd('\\').ToUpperInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedIdentity));
        var name = Convert.ToHexString(bytes.AsSpan(0, 12)).ToLowerInvariant();
        return $"volume-{name}";
    }
}
