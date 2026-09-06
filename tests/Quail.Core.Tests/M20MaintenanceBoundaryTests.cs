using System.ComponentModel;
using System.Text;
using Quail.FileSystem;

namespace Quail.Core.Tests;

public sealed class M20MaintenanceBoundaryTests : IDisposable
{
    private const string Volume = @"\\?\Volume{01234567-89ab-cdef-0123-456789abcdef}";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"quail-m20-{Guid.NewGuid():N}");

    [Fact]
    public void Continuity_rejects_wrong_journal_below_range_and_future_checkpoint()
    {
        Assert.False(IndexStore.TryValidateContinuity(new(2, 100, 10, 10), new(1, 10, 200, 10, 2, 3), out var idReason));
        Assert.Equal("journal-id-mismatch", idReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 9, 0, 0), new(1, 10, 200, 10, 2, 3), out var lowerReason));
        Assert.Equal("saved-usn-before-readable-range", lowerReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 201, 10, 10), new(1, 10, 200, 10, 2, 3), out var upperReason));
        Assert.Equal("saved-usn-after-journal-frontier", upperReason);
        Assert.False(IndexStore.TryValidateContinuity(new(1, 9, 10, 8), new(1, 0, 200, 0, 2, 3), out var checkpointReason));
        Assert.Equal("checkpoint-lower-bounds-inconsistent", checkpointReason);
    }

    [Fact]
    public void Protected_index_rejects_non_service_writer_before_touching_disk()
    {
        var store = new IndexStore(ManagedIndexPath.ForVolumeIdentity(Volume));
        Assert.Throws<UnauthorizedAccessException>(() => store.BuildFromRecords(
            new VolumeDescriptor(Volume, "Q:\\", "NTFS", "Test"),
            _ => { }));
    }

    [Fact]
    public void Maintenance_state_round_trips_atomically_and_rejects_invalid_target()
    {
        Directory.CreateDirectory(_directory);
        var store = new MaintenanceStateStore(
            Path.Combine(_directory, "targets.json"),
            Path.Combine(_directory, "health.json"));
        var targets = new MaintenanceTargetsDocument(1, 7, [new(Volume, "Q:\\")]);
        store.SaveTargets(targets);
        var loaded = store.LoadTargets();
        Assert.Equal(targets.Version, loaded.Version);
        Assert.Equal(targets.Generation, loaded.Generation);
        Assert.Equal(targets.Targets, loaded.Targets);
        Assert.Throws<InvalidDataException>(() => store.SaveTargets(new(1, 8, [new("C:\\", null)])));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Control_framing_rejects_oversized_duplicate_and_stale_requests()
    {
        var stale = new MaintenanceControlRequest(1, Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(-1), MaintenanceControlCommand.Rebuild, Volume, null);
        Assert.Equal("stale-request", MaintenanceControlValidation.Validate(stale, DateTimeOffset.UtcNow));

        await using var oversized = new MemoryStream();
        await oversized.WriteAsync(BitConverter.GetBytes(MaintenanceControlValidation.MaximumFrameBytes + 1));
        oversized.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MaintenanceControlFraming.ReadRequestAsync(oversized, CancellationToken.None));

        var duplicateJson = Encoding.UTF8.GetBytes("{\"Version\":1,\"Version\":1}");
        await using var duplicate = new MemoryStream();
        await duplicate.WriteAsync(BitConverter.GetBytes(duplicateJson.Length));
        await duplicate.WriteAsync(duplicateJson);
        duplicate.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => MaintenanceControlFraming.ReadRequestAsync(duplicate, CancellationToken.None));
    }

    [Fact]
    public void Journal_failure_classification_preserves_transient_and_fails_closed_on_proven_loss()
    {
        Assert.True(IndexStore.IsTransientJournalFailure(new Win32Exception(1178)));
        Assert.False(IndexStore.IsTransientJournalFailure(new Win32Exception(1179)));
        Assert.False(IndexStore.IsTransientJournalFailure(new Win32Exception(1181)));
    }

    [Fact]
    public async Task Native_pipe_acl_rejects_a_non_elevated_client_before_framing()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var server = new MaintenanceControlServer((request, _) => Task.FromResult(new MaintenanceControlResponse(
            MaintenanceControlResponse.CurrentVersion,
            request.RequestId,
            MaintenanceControlStatus.Accepted,
            Guid.NewGuid(),
            null)));
        var serverTask = Task.Run(() => server.RunAsync(cancellation.Token));
        var request = new MaintenanceControlRequest(
            MaintenanceControlRequest.CurrentVersion,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            MaintenanceControlCommand.RegisterAndBuild,
            Volume,
            null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new MaintenanceControlClient().SendAsync(request, cancellation.Token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => serverTask);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
