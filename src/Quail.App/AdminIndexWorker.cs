using System.Diagnostics;
using System.Security.Principal;
using Quail.FileSystem;

namespace Quail.App;

internal enum AdminIndexOperation { Build, Rebuild, Refresh }

internal sealed record AdminOperationRequest(Guid Id, AdminIndexOperation Operation, string MountPoint, string VolumeIdentity);

internal sealed record AdminOperationResult(
    Guid OperationId,
    string Operation,
    bool Success,
    bool RebuildRequired,
    long? Records,
    long? RecordsApplied,
    double ElapsedMilliseconds,
    string? Detail,
    string? Status);

internal static class AdminIndexWorker
{
    internal const int SuccessExitCode = 0;
    internal const int FailureExitCode = 1;
    internal const int RebuildRequiredExitCode = 3;
    internal const int ElevationRejectedExitCode = 10;
    internal const int VolumeRejectedExitCode = 11;
    internal const int CatalogRejectedExitCode = 12;
    internal const int StorageRejectedExitCode = 13;
    internal const int IndexOperationFailedExitCode = 14;

    public static bool TryParse(string[] arguments, out AdminOperationRequest? request, out string? error)
    {
        request = null;
        error = null;
        if (!arguments.Contains("--internal-index-operation", StringComparer.Ordinal)) return false;
        string? operation = null, id = null, mount = null, identity = null;
        for (var index = 0; index < arguments.Length; index++)
        {
            var argument = arguments[index];
            if (argument is not "--internal-index-operation" and not "--internal-operation-id" and not "--internal-mount-point" and not "--internal-volume-identity") { error = $"Unknown internal worker argument '{argument}'."; return true; }
            if (++index >= arguments.Length || arguments[index].StartsWith("--", StringComparison.Ordinal)) { error = $"{argument} requires a value."; return true; }
            switch (argument)
            {
                case "--internal-index-operation": operation = arguments[index]; break;
                case "--internal-operation-id": id = arguments[index]; break;
                case "--internal-mount-point": mount = arguments[index]; break;
                case "--internal-volume-identity": identity = arguments[index]; break;
            }
        }
        if (!Guid.TryParse(id, out var parsedId)) { error = "Internal worker requires a valid operation GUID."; return true; }
        if (!Enum.TryParse<AdminIndexOperation>(operation, true, out var parsedOperation)) { error = "Unknown index operation."; return true; }
        if (string.IsNullOrWhiteSpace(mount) || string.IsNullOrWhiteSpace(identity)) { error = "Internal worker requires a mount point and volume identity."; return true; }
        request = new(parsedId, parsedOperation, mount, identity);
        return true;
    }

    public static int Run(AdminOperationRequest request)
    {
        if (!IsElevated()) return ElevationRejectedExitCode;

        var outcome = FileSystemIndexAdministration.Run(new FileSystemIndexOperationRequest(
            request.Operation switch
            {
                AdminIndexOperation.Build => FileSystemIndexOperation.Build,
                AdminIndexOperation.Rebuild => FileSystemIndexOperation.Rebuild,
                AdminIndexOperation.Refresh => FileSystemIndexOperation.Refresh,
                _ => throw new ArgumentOutOfRangeException(nameof(request))
            },
            request.MountPoint,
            request.VolumeIdentity));

        return outcome switch
        {
            FileSystemIndexOperationOutcome.Succeeded => SuccessExitCode,
            FileSystemIndexOperationOutcome.RebuildRequired => RebuildRequiredExitCode,
            FileSystemIndexOperationOutcome.VolumeRejected => VolumeRejectedExitCode,
            FileSystemIndexOperationOutcome.CatalogRejected => CatalogRejectedExitCode,
            FileSystemIndexOperationOutcome.StorageRejected => StorageRejectedExitCode,
            FileSystemIndexOperationOutcome.IndexOperationFailed => IndexOperationFailedExitCode,
            _ => FailureExitCode
        };
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

internal sealed class ElevatedIndexOperationRunner
{
    internal static ProcessStartInfo CreateProcessStartInfo(string executable, AdminIndexOperation operation, Guid id, IndexCatalogEntry entry)
    {
        var startInfo = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" };
        startInfo.ArgumentList.Add("--internal-index-operation");
        startInfo.ArgumentList.Add(operation.ToString());
        startInfo.ArgumentList.Add("--internal-operation-id");
        startInfo.ArgumentList.Add(id.ToString("D"));
        startInfo.ArgumentList.Add("--internal-mount-point");
        startInfo.ArgumentList.Add(entry.MountPoint);
        startInfo.ArgumentList.Add("--internal-volume-identity");
        startInfo.ArgumentList.Add(entry.VolumeIdentity);
        return startInfo;
    }

    public async Task<AdminOperationResult> RunAsync(AdminIndexOperation operation, IndexCatalogEntry entry)
    {
        var id = Guid.NewGuid();
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The Quail executable path is unavailable.");
        var stopwatch = Stopwatch.StartNew();
        int exitCode;
        try
        {
            using var process = Process.Start(CreateProcessStartInfo(executable, operation, id, entry))
                ?? throw new InvalidOperationException("Could not start the administrator operation.");
            await process.WaitForExitAsync();
            exitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            return new(id, operation.ToString(), false, false, null, null, 0, "Administrator approval was canceled.", "Canceled");
        }

        stopwatch.Stop();
        if (exitCode == AdminIndexWorker.RebuildRequiredExitCode)
        {
            return new(id, operation.ToString(), true, true, null, null, stopwatch.Elapsed.TotalMilliseconds, "Refresh requires an explicit rebuild.", "RebuildRequired");
        }

        if (exitCode != AdminIndexWorker.SuccessExitCode)
        {
            return new(id, operation.ToString(), false, false, null, null, stopwatch.Elapsed.TotalMilliseconds, "The administrator operation failed or another operation is already running for this volume.", "Error");
        }

        var status = FileSystemIndexAdministration.GetStatus(entry.DatabasePath);
        return new(id, operation.ToString(), true, false, status.RecordCount, null, stopwatch.Elapsed.TotalMilliseconds, null, status.State.ToString());
    }
}
