namespace Quail.FileSystem;

public sealed class IndexCatalogController
{
    private readonly IIndexCatalogStore _store;
    private readonly Func<string, VolumeDescriptor> _validateVolume;
    private readonly Func<string, IndexStatus> _readStatus;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly Dictionary<string, long> _entryRevisions = new(StringComparer.OrdinalIgnoreCase);
    private IndexCatalogDocument _catalog = IndexCatalogDocument.Empty;
    private string? _loadError;
    private string[] _activePaths = [];

    public IndexCatalogController(IIndexCatalogStore? store = null, Func<string, VolumeDescriptor>? validateVolume = null, Func<string, IndexStatus>? readStatus = null)
    {
        _store = store ?? new IndexCatalogStore();
        _validateVolume = validateVolume ?? NtfsVolume.Validate;
        _readStatus = readStatus ?? FileSystemIndexAdministration.GetStatus;
    }

    public string? LoadError { get { lock (_gate) return _loadError; } }
    public IReadOnlyList<IndexCatalogEntry> Entries { get { lock (_gate) return _catalog.Entries.ToArray(); } }
    public IReadOnlyList<string> ActivePaths => Volatile.Read(ref _activePaths);
    public event Action? ActivePathsChanged;

    public async Task LoadAsync()
    {
        var loaded = await _store.LoadAsync();
        bool changed;
        lock (_gate)
        {
            _catalog = loaded.Catalog;
            _loadError = loaded.Error;
            _entryRevisions.Clear();
            foreach (var entry in _catalog.Entries) _entryRevisions[entry.VolumeIdentity] = 0;
            changed = UpdateActivePathsLocked();
        }
        if (changed) ActivePathsChanged?.Invoke();
    }

    public async Task AddAsync(VolumeDescriptor volume)
    {
        await MutateAsync(catalog =>
        {
            if (catalog.Entries.Any(entry => Same(entry, volume.StableIdentity)))
                throw new InvalidOperationException("This volume is already configured.");
            var entry = new IndexCatalogEntry(volume.StableIdentity, volume.MountPoint, ManagedIndexPath.ForVolumeIdentity(volume.StableIdentity), false);
            return catalog with { Entries = catalog.Entries.Append(entry).ToArray() };
        }, volume.StableIdentity);
    }

    public async Task SetEnabledAsync(string volumeIdentity, bool enabled)
    {
        await MutateAsync(catalog =>
        {
            if (!catalog.Entries.Any(entry => Same(entry, volumeIdentity)))
                throw new InvalidOperationException("The configured index no longer exists.");
            return catalog with { Entries = catalog.Entries.Select(entry => Same(entry, volumeIdentity) ? entry with { EnabledForSearch = enabled } : entry).ToArray() };
        }, volumeIdentity);
    }

    public Task RemoveAsync(string volumeIdentity) => MutateAsync(
        catalog => catalog with { Entries = catalog.Entries.Where(entry => !Same(entry, volumeIdentity)).ToArray() },
        volumeIdentity);

    public bool IsConfigured(string volumeIdentity) => Entries.Any(entry => string.Equals(entry.VolumeIdentity, volumeIdentity, StringComparison.OrdinalIgnoreCase));

    public void ReevaluateActivePaths()
    {
        bool changed;
        lock (_gate) changed = UpdateActivePathsLocked();
        if (changed) ActivePathsChanged?.Invoke();
    }

    public (IndexCatalogEntry Entry, long Revision)? GetEntrySnapshot(string volumeIdentity)
    {
        lock (_gate)
        {
            var entry = _catalog.Entries.FirstOrDefault(candidate => Same(candidate, volumeIdentity));
            return entry is null ? null : (entry, _entryRevisions.GetValueOrDefault(volumeIdentity));
        }
    }

    public async Task<bool> TryEnableAfterInitialBuildAsync(string volumeIdentity, long expectedRevision)
    {
        await _mutationGate.WaitAsync();
        try
        {
            IndexCatalogDocument candidate;
            lock (_gate)
            {
                if (!_entryRevisions.TryGetValue(volumeIdentity, out var revision) || revision != expectedRevision)
                    return false;
                var entry = _catalog.Entries.FirstOrDefault(item => Same(item, volumeIdentity));
                if (entry is null || entry.EnabledForSearch)
                    return entry is not null;
                candidate = _catalog with { Entries = _catalog.Entries.Select(item => Same(item, volumeIdentity) ? item with { EnabledForSearch = true } : item).ToArray() };
            }

            await _store.SaveAsync(candidate);
            Commit(candidate, volumeIdentity);
            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task MutateAsync(Func<IndexCatalogDocument, IndexCatalogDocument> mutation, string changedIdentity)
    {
        await _mutationGate.WaitAsync();
        try
        {
            IndexCatalogDocument candidate;
            lock (_gate) candidate = mutation(_catalog);
            await _store.SaveAsync(candidate);
            Commit(candidate, changedIdentity);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void Commit(IndexCatalogDocument candidate, string changedIdentity)
    {
        bool activePathsChanged;
        lock (_gate)
        {
            _catalog = candidate;
            _loadError = null;
            _entryRevisions[changedIdentity] = _entryRevisions.GetValueOrDefault(changedIdentity) + 1;
            activePathsChanged = UpdateActivePathsLocked();
        }
        if (activePathsChanged) ActivePathsChanged?.Invoke();
    }

    private bool UpdateActivePathsLocked()
    {
        var candidate = _catalog.Entries.Where(entry => entry.EnabledForSearch && IsCompatibleComplete(entry)).Select(entry => entry.DatabasePath).ToArray();
        if (_activePaths.SequenceEqual(candidate, StringComparer.OrdinalIgnoreCase))
            return false;
        Volatile.Write(ref _activePaths, candidate);
        return true;
    }

    private bool IsCompatibleComplete(IndexCatalogEntry entry)
    {
        try
        {
            var currentVolume = _validateVolume(entry.MountPoint);
            if (!string.Equals(currentVolume.StableIdentity, entry.VolumeIdentity, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var status = _readStatus(entry.DatabasePath);
            return status.State == IndexState.Complete &&
                   string.Equals(status.VolumeIdentity, entry.VolumeIdentity, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(status.VolumeIdentity, currentVolume.StableIdentity, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static bool Same(IndexCatalogEntry entry, string identity) => string.Equals(entry.VolumeIdentity, identity, StringComparison.OrdinalIgnoreCase);
}
