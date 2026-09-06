namespace Quail.FileSystem;

public enum MaintenanceHealthState
{
    Healthy,
    CatchingUp,
    Unavailable,
    Retrying,
    Error,
    RebuildRequired
}

public sealed record MaintenanceTarget(string VolumeIdentity, string? LastKnownMountPoint);

public sealed record MaintenanceTargetsDocument(
    int Version,
    long Generation,
    IReadOnlyList<MaintenanceTarget> Targets)
{
    public const int CurrentVersion = 1;
    public static readonly MaintenanceTargetsDocument Empty = new(CurrentVersion, 0, []);
}

public sealed record MaintenanceTargetHealth(
    string VolumeIdentity,
    MaintenanceHealthState State,
    bool TrustedForSearch,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastSuccessfulMaintenanceUtc,
    IncrementalCheckpoint? Checkpoint,
    Guid? OperationId,
    string? Reason);

public sealed record MaintenanceHealthDocument(
    int Version,
    long ConfigurationGeneration,
    IReadOnlyList<MaintenanceTargetHealth> Targets)
{
    public const int CurrentVersion = 1;
    public static readonly MaintenanceHealthDocument Empty = new(CurrentVersion, 0, []);
}

public enum MaintenanceControlCommand
{
    RegisterAndBuild,
    Rebuild,
    Unregister,
    GetOperationStatus
}

public enum MaintenanceControlStatus
{
    Accepted,
    Busy,
    Succeeded,
    RebuildRequired,
    Unavailable,
    Rejected,
    Error
}

public sealed record MaintenanceControlRequest(
    int Version,
    Guid RequestId,
    DateTimeOffset IssuedUtc,
    MaintenanceControlCommand Command,
    string? VolumeIdentity,
    Guid? OperationId)
{
    public const int CurrentVersion = 1;
}

public sealed record MaintenanceControlResponse(
    int Version,
    Guid RequestId,
    MaintenanceControlStatus Status,
    Guid? OperationId,
    string? Diagnostic)
{
    public const int CurrentVersion = 1;
}

internal sealed record MaintenanceOperationSnapshot(
    Guid OperationId,
    string VolumeIdentity,
    MaintenanceControlCommand Command,
    MaintenanceControlStatus Status,
    string? Diagnostic);
