using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quail.FileSystem;

public sealed class MaintenanceStateStore
{
    private const int MaximumDocumentBytes = 1024 * 1024;
    private const int MaximumTargets = 128;
    private const int MaximumReasonLength = 512;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _targetsPath;
    private readonly string _healthPath;
    private readonly bool _protectMachinePaths;

    public MaintenanceStateStore()
        : this(
            PrivilegedIndexStorage.MaintenanceTargetsPath,
            PrivilegedIndexStorage.MaintenanceHealthPath,
            protectMachinePaths: true)
    {
    }

    internal MaintenanceStateStore(string targetsPath, string healthPath, bool protectMachinePaths = false)
    {
        _targetsPath = Path.GetFullPath(targetsPath);
        _healthPath = Path.GetFullPath(healthPath);
        _protectMachinePaths = protectMachinePaths;
    }

    public MaintenanceTargetsDocument LoadTargets() => Read(
        _targetsPath,
        MaintenanceTargetsDocument.Empty,
        ValidateTargets);

    public MaintenanceHealthDocument LoadHealth() => Read(
        _healthPath,
        MaintenanceHealthDocument.Empty,
        ValidateHealth);

    public void SaveTargets(MaintenanceTargetsDocument document)
    {
        ValidateTargets(document);
        WriteAtomically(_targetsPath, document);
    }

    public void SaveHealth(MaintenanceHealthDocument document)
    {
        ValidateHealth(document);
        WriteAtomically(_healthPath, document);
    }

    public MaintenanceTargetHealth? GetHealth(string volumeIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeIdentity);
        try
        {
            var targets = LoadTargets();
            var health = LoadHealth();
            if (health.ConfigurationGeneration != targets.Generation ||
                !targets.Targets.Any(target => string.Equals(target.VolumeIdentity, volumeIdentity, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            return health.Targets.SingleOrDefault(target =>
                string.Equals(target.VolumeIdentity, volumeIdentity, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private T Read<T>(string path, T missingValue, Action<T> validate)
    {
        using var lease = AcquireProtection();
        if (!File.Exists(path))
        {
            return missingValue;
        }

        var length = new FileInfo(path).Length;
        if (length is <= 0 or > MaximumDocumentBytes)
        {
            throw new InvalidDataException("Maintenance state has an invalid size.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var value = JsonSerializer.Deserialize<T>(stream, SerializerOptions)
            ?? throw new InvalidDataException("Maintenance state is empty.");
        validate(value);
        return value;
    }

    private void WriteAtomically<T>(string path, T value)
    {
        using var lease = AcquireProtection();
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Maintenance state requires a parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       16 * 1024,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            if (_protectMachinePaths)
            {
                PrivilegedIndexStorage.ValidateNotReparseIfPresent(temporaryPath);
                PrivilegedIndexStorage.ValidateNotReparseIfPresent(path);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private IDisposable? AcquireProtection() =>
        _protectMachinePaths ? PrivilegedIndexStorage.AcquireMachineState() : null;

    private static void ValidateTargets(MaintenanceTargetsDocument document)
    {
        if (document.Version != MaintenanceTargetsDocument.CurrentVersion ||
            document.Generation < 0 ||
            document.Targets is null ||
            document.Targets.Count > MaximumTargets ||
            document.Targets.Any(target =>
                !MaintenanceControlValidation.IsCanonicalVolumeIdentity(target.VolumeIdentity) ||
                target.LastKnownMountPoint is { Length: > 64 }) ||
            document.Targets.GroupBy(target => target.VolumeIdentity, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("Maintenance targets are invalid.");
        }
    }

    private static void ValidateHealth(MaintenanceHealthDocument document)
    {
        if (document.Version != MaintenanceHealthDocument.CurrentVersion ||
            document.ConfigurationGeneration < 0 ||
            document.Targets is null ||
            document.Targets.Count > MaximumTargets ||
            document.Targets.Any(target =>
                !MaintenanceControlValidation.IsCanonicalVolumeIdentity(target.VolumeIdentity) ||
                target.Reason is { Length: > MaximumReasonLength }) ||
            document.Targets.GroupBy(target => target.VolumeIdentity, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() != 1))
        {
            throw new InvalidDataException("Maintenance health is invalid.");
        }
    }
}
