using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

internal readonly record struct UsnJournalWaitCompletion(int ErrorCode, uint BytesReturned);

internal interface IUsnJournalWaitOperation : IDisposable
{
    void Start();

    void Cancel();
}

internal interface IUsnJournalWaitBackend
{
    IUsnJournalWaitOperation Create(
        SafeFileHandle volumeHandle,
        NtfsJournal.ReadUsnJournalDataV1 request,
        int outputBufferSize,
        Action<UsnJournalWaitCompletion> completion);
}

internal static class UsnJournalWait
{
    private const int ErrorOperationAborted = 995;
    private const int WaitBufferSize = 64 * 1024;

    private static readonly IUsnJournalWaitBackend DefaultBackend = new WindowsUsnJournalWaitBackend();

    internal static Task WaitAsync(
        SafeFileHandle volumeHandle,
        IncrementalCheckpoint checkpoint,
        CancellationToken cancellationToken) =>
        WaitAsync(volumeHandle, checkpoint, cancellationToken, DefaultBackend);

    internal static Task WaitAsync(
        SafeFileHandle volumeHandle,
        IncrementalCheckpoint checkpoint,
        CancellationToken cancellationToken,
        IUsnJournalWaitBackend backend)
    {
        ArgumentNullException.ThrowIfNull(volumeHandle);
        ArgumentNullException.ThrowIfNull(backend);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(cancellationToken);
        }

        var request = new NtfsJournal.ReadUsnJournalDataV1(
            StartUsn: checkpoint.NextUsn,
            ReasonMask: uint.MaxValue,
            ReturnOnlyOnClose: 0,
            Timeout: 0,
            BytesToWait: 1,
            UsnJournalId: checkpoint.JournalId,
            MinMajorVersion: 2,
            MaxMajorVersion: 3);

        var pending = new PendingWait(cancellationToken);
        var operation = backend.Create(
            volumeHandle,
            request,
            WaitBufferSize,
            pending.Complete);
        try
        {
            pending.Attach(operation);
            pending.RegisterCancellation();
            pending.Start();
        }
        catch (Exception exception)
        {
            pending.Fail(exception);
        }

        return pending.Task;
    }

    private sealed class PendingWait(CancellationToken cancellationToken)
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _cancellationToken = cancellationToken;
        private IUsnJournalWaitOperation? _operation;
        private CancellationTokenRegistration _registration;
        private bool _registrationCreated;
        private bool _started;
        private bool _cancelRequested;
        private bool _cancelIssued;
        private bool _completed;

        internal Task Task => _completion.Task;

        internal void Attach(IUsnJournalWaitOperation operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            lock (_gate)
            {
                _operation = operation;
            }
        }

        internal void RegisterCancellation()
        {
            _registration = _cancellationToken.Register(
                static state => ((PendingWait)state!).RequestCancellation(),
                this);
            lock (_gate)
            {
                _registrationCreated = true;
            }
        }

        internal void Start()
        {
            IUsnJournalWaitOperation? cancelOperation = null;
            Exception? failure = null;

            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                try
                {
                    _operation!.Start();
                    _started = true;
                    if (_cancelRequested && !_cancelIssued)
                    {
                        _cancelIssued = true;
                        cancelOperation = _operation;
                    }
                }
                catch (Exception exception)
                {
                    _completed = true;
                    failure = exception;
                }
            }

            if (failure is not null)
            {
                Finish(failure);
            }
            else if (cancelOperation is not null)
            {
                CancelNative(cancelOperation);
            }
        }

        internal void RequestCancellation()
        {
            IUsnJournalWaitOperation? cancelOperation = null;

            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _cancelRequested = true;
                if (_started && !_cancelIssued)
                {
                    _cancelIssued = true;
                    cancelOperation = _operation;
                }
            }

            if (cancelOperation is not null)
            {
                CancelNative(cancelOperation);
            }
        }

        internal void Complete(UsnJournalWaitCompletion completion)
        {
            Exception? failure = null;
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
                if (completion.ErrorCode == 0)
                {
                    // The output is deliberately ignored. A completion only signals
                    // that authoritative journal validation and Sync should run.
                }
                else if (completion.ErrorCode == ErrorOperationAborted &&
                         (_cancelRequested || _cancellationToken.IsCancellationRequested))
                {
                    failure = new OperationCanceledException(_cancellationToken);
                }
                else
                {
                    failure = new Win32Exception(completion.ErrorCode, "FSCTL_READ_USN_JOURNAL wait");
                }
            }

            Finish(failure);
        }

        private void CancelNative(IUsnJournalWaitOperation operation)
        {
            try
            {
                operation.Cancel();
            }
            catch (Exception)
            {
                // The kernel may still own the OVERLAPPED request when cancellation
                // itself fails. Its completion remains the only safe point at which
                // the operation can release native buffers and the OVERLAPPED block.
            }
        }

        internal void Fail(Exception exception)
        {
            lock (_gate)
            {
                if (_completed)
                {
                    return;
                }

                _completed = true;
            }

            Finish(exception);
        }

        private void Finish(Exception? failure)
        {
            IUsnJournalWaitOperation? operation;
            CancellationTokenRegistration registration;
            bool registrationCreated;

            lock (_gate)
            {
                operation = _operation;
                registration = _registration;
                registrationCreated = _registrationCreated;
            }

            operation?.Dispose();
            if (registrationCreated)
            {
                registration.Dispose();
            }

            if (failure is OperationCanceledException)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
            else if (failure is not null)
            {
                _completion.TrySetException(failure);
            }
            else
            {
                _completion.TrySetResult();
            }
        }
    }

    private sealed class WindowsUsnJournalWaitBackend : IUsnJournalWaitBackend
    {
        public IUsnJournalWaitOperation Create(
            SafeFileHandle volumeHandle,
            NtfsJournal.ReadUsnJournalDataV1 request,
            int outputBufferSize,
            Action<UsnJournalWaitCompletion> completion) =>
            new WindowsUsnJournalWaitOperation(volumeHandle, request, outputBufferSize, completion);
    }

    private sealed unsafe class WindowsUsnJournalWaitOperation : IUsnJournalWaitOperation
    {
        private const int ErrorIoPending = 997;
        private const int ErrorNotFound = 1168;

        private readonly object _gate = new();
        private readonly SafeFileHandle _volumeHandle;
        private readonly ThreadPoolBoundHandle _boundHandle;
        private readonly Action<UsnJournalWaitCompletion> _completion;
        private readonly int _outputBufferSize;
        private IntPtr _input;
        private IntPtr _output;
        private NativeOverlapped* _overlapped;
        private bool _disposed;

        internal WindowsUsnJournalWaitOperation(
            SafeFileHandle volumeHandle,
            NtfsJournal.ReadUsnJournalDataV1 request,
            int outputBufferSize,
            Action<UsnJournalWaitCompletion> completion)
        {
            ArgumentNullException.ThrowIfNull(volumeHandle);
            ArgumentNullException.ThrowIfNull(completion);
            if (outputBufferSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(outputBufferSize));
            }

            _volumeHandle = volumeHandle;
            _completion = completion;
            _outputBufferSize = outputBufferSize;
            _boundHandle = ThreadPoolBoundHandle.BindHandle(volumeHandle);
            try
            {
                var inputSize = Marshal.SizeOf<NtfsJournal.ReadUsnJournalDataV1>();
                _input = Marshal.AllocHGlobal(inputSize);
                Marshal.StructureToPtr(request, _input, fDeleteOld: false);
                _output = Marshal.AllocHGlobal(outputBufferSize);
                _overlapped = _boundHandle.AllocateNativeOverlapped(CompletionCallback, this, null);
            }
            catch
            {
                ReleaseNativeResources();
                throw;
            }
        }

        public void Start()
        {
            lock (_gate)
            {
                ThrowIfDisposed();
                if (!DeviceIoControl(
                        _volumeHandle,
                        NtfsJournal.FsctlReadUsnJournal,
                        _input,
                        (uint)Marshal.SizeOf<NtfsJournal.ReadUsnJournalDataV1>(),
                        _output,
                        (uint)_outputBufferSize,
                        out _,
                        _overlapped))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != ErrorIoPending)
                    {
                        throw new Win32Exception(error, "FSCTL_READ_USN_JOURNAL");
                    }
                }
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                if (!CancelIoEx(_volumeHandle, _overlapped))
                {
                    var error = Marshal.GetLastWin32Error();
                    // ERROR_NOT_FOUND means the request has already completed or
                    // is completing; its completion callback remains authoritative.
                    if (error != ErrorNotFound)
                    {
                        throw new Win32Exception(error, "CancelIoEx");
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                ReleaseNativeResources();
            }
        }

        private void Complete(uint errorCode, uint bytesReturned)
        {
            _completion(new UsnJournalWaitCompletion(checked((int)errorCode), bytesReturned));
        }

        private void ReleaseNativeResources()
        {
            if (_overlapped != null)
            {
                _boundHandle.FreeNativeOverlapped(_overlapped);
                _overlapped = null;
            }

            if (_output != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_output);
                _output = IntPtr.Zero;
            }

            if (_input != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_input);
                _input = IntPtr.Zero;
            }

            _boundHandle.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(WindowsUsnJournalWaitOperation));
            }
        }

        private static unsafe void CompletionCallback(
            uint errorCode,
            uint bytesReturned,
            NativeOverlapped* overlapped)
        {
            var operation = (WindowsUsnJournalWaitOperation?)ThreadPoolBoundHandle.GetNativeOverlappedState(overlapped);
            operation?.Complete(errorCode, bytesReturned);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool DeviceIoControl(
            SafeFileHandle handle,
            uint code,
            IntPtr input,
            uint inputSize,
            IntPtr output,
            uint outputSize,
            out uint returned,
            NativeOverlapped* overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool CancelIoEx(
            SafeFileHandle handle,
            NativeOverlapped* overlapped);
    }
}
