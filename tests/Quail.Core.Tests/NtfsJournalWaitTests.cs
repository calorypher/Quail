using System.ComponentModel;
using Microsoft.Win32.SafeHandles;

namespace Quail.Core.Tests;

public sealed class NtfsJournalWaitTests
{
    [Fact]
    public async Task UsesCommittedFrontierAndCompletesAsSignalOnly()
    {
        using var handle = new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        var backend = new FakeBackend();
        var checkpoint = new IncrementalCheckpoint(0x1234, 456, 10, 20);

        var wait = UsnJournalWait.WaitAsync(handle, checkpoint, CancellationToken.None, backend);

        Assert.NotNull(backend.Request);
        Assert.Equal(checkpoint.NextUsn, backend.Request.Value.StartUsn);
        Assert.Equal(uint.MaxValue, backend.Request.Value.ReasonMask);
        Assert.Equal((ulong)0, backend.Request.Value.Timeout);
        Assert.Equal((ulong)1, backend.Request.Value.BytesToWait);
        Assert.Equal(checkpoint.JournalId, backend.Request.Value.UsnJournalId);
        Assert.Equal((ushort)2, backend.Request.Value.MinMajorVersion);
        Assert.Equal((ushort)3, backend.Request.Value.MaxMajorVersion);
        Assert.True(backend.OutputBufferSize > 0);
        Assert.True(backend.Operation!.Started);
        Assert.False(backend.Operation.Disposed);

        // The completion payload is intentionally not exposed to the caller.
        backend.Operation.Complete(errorCode: 0, bytesReturned: 37);
        await wait;

        Assert.True(backend.Operation.Disposed);
    }

    [Fact]
    public async Task CancellationCancelsExactlyOnceAndMapsOperationAborted()
    {
        using var handle = new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        using var cancellation = new CancellationTokenSource();
        var backend = new FakeBackend();
        var wait = UsnJournalWait.WaitAsync(
            handle,
            new IncrementalCheckpoint(9, 100, 1, 1),
            cancellation.Token,
            backend);

        cancellation.Cancel();

        Assert.Equal(1, backend.Operation!.CancelCalls);
        backend.Operation.Complete(errorCode: 995, bytesReturned: 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
        Assert.True(backend.Operation.Disposed);
    }

    [Fact]
    public async Task OperationAbortedWithoutRequestedCancellationIsAnError()
    {
        using var handle = new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        var backend = new FakeBackend();
        var wait = UsnJournalWait.WaitAsync(
            handle,
            new IncrementalCheckpoint(9, 100, 1, 1),
            CancellationToken.None,
            backend);

        backend.Operation!.Complete(errorCode: 995, bytesReturned: 0);

        var exception = await Assert.ThrowsAsync<Win32Exception>(() => wait);
        Assert.Equal(995, exception.NativeErrorCode);
        Assert.True(backend.Operation.Disposed);
    }

    [Fact]
    public async Task NativeErrorIsPropagatedAndResourcesRemainUntilCompletion()
    {
        using var handle = new SafeFileHandle(new IntPtr(1), ownsHandle: false);
        var backend = new FakeBackend();
        var wait = UsnJournalWait.WaitAsync(
            handle,
            new IncrementalCheckpoint(9, 100, 1, 1),
            CancellationToken.None,
            backend);

        Assert.False(backend.Operation!.Disposed);
        backend.Operation.Complete(errorCode: 5, bytesReturned: 0);

        var exception = await Assert.ThrowsAsync<Win32Exception>(() => wait);
        Assert.Equal(5, exception.NativeErrorCode);
        Assert.True(backend.Operation.Disposed);
    }

    private sealed class FakeBackend : IUsnJournalWaitBackend
    {
        public NtfsJournal.ReadUsnJournalDataV1? Request { get; private set; }
        public int OutputBufferSize { get; private set; }
        public FakeOperation? Operation { get; private set; }

        public IUsnJournalWaitOperation Create(
            SafeFileHandle volumeHandle,
            NtfsJournal.ReadUsnJournalDataV1 request,
            int outputBufferSize,
            Action<UsnJournalWaitCompletion> completion)
        {
            Request = request;
            OutputBufferSize = outputBufferSize;
            Operation = new FakeOperation(completion);
            return Operation;
        }
    }

    private sealed class FakeOperation(Action<UsnJournalWaitCompletion> completion) : IUsnJournalWaitOperation
    {
        private readonly Action<UsnJournalWaitCompletion> _completion = completion;

        public bool Started { get; private set; }
        public bool Disposed { get; private set; }
        public int CancelCalls { get; private set; }

        public void Start() => Started = true;

        public void Cancel() => CancelCalls++;

        public void Complete(int errorCode, uint bytesReturned) =>
            _completion(new UsnJournalWaitCompletion(errorCode, bytesReturned));

        public void Dispose() => Disposed = true;
    }
}
