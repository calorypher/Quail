using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Quail.FileSystem;

public sealed class MaintenanceControlClient
{
    public const string PipeName = "Quail.Maintenance.v1";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task<MaintenanceControlResponse> SendAsync(
        MaintenanceControlRequest request,
        CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        await using var pipe = new NamedPipeClientStream(
            ".",
            PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous,
            TokenImpersonationLevel.Identification);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await MaintenanceControlFraming.WriteRequestAsync(pipe, request, deadline.Token).ConfigureAwait(false);
        var response = await MaintenanceControlFraming.ReadResponseAsync(pipe, deadline.Token).ConfigureAwait(false);
        if (response.Version != MaintenanceControlResponse.CurrentVersion || response.RequestId != request.RequestId)
        {
            throw new InvalidDataException("The maintenance service returned a mismatched response.");
        }

        return response;
    }
}

public sealed class MaintenanceControlServer
{
    private static readonly TimeSpan ConnectionDeadline = TimeSpan.FromSeconds(10);
    private readonly Func<MaintenanceControlRequest, CancellationToken, Task<MaintenanceControlResponse>> _handler;
    private readonly MaintenanceRequestReplayGuard _replayGuard = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Action<string>? _diagnostic;

    public MaintenanceControlServer(
        Func<MaintenanceControlRequest, CancellationToken, Task<MaintenanceControlResponse>> handler,
        Func<DateTimeOffset>? utcNow = null,
        Action<string>? diagnostic = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _diagnostic = diagnostic;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = MaintenancePipeFactory.CreateServer();
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            _diagnostic?.Invoke("control-connected");
            await ProcessConnectionAsync(pipe, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessConnectionAsync(NamedPipeServerStream pipe, CancellationToken serviceCancellation)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(serviceCancellation);
        deadline.CancelAfter(ConnectionDeadline);
        try
        {
            var request = await MaintenanceControlFraming.ReadRequestAsync(pipe, deadline.Token).ConfigureAwait(false);
            _diagnostic?.Invoke("control-request-read");
            var now = _utcNow();
            var validationError = MaintenanceControlValidation.Validate(request, now);
            MaintenanceControlResponse response;
            var authorized = MaintenancePipeAuthorization.IsAuthorized(pipe);
            _diagnostic?.Invoke(authorized ? "control-authorized" : "control-unauthorized");
            if (!authorized)
            {
                response = Rejected(request, "unauthorized-caller");
            }
            else if (validationError is not null)
            {
                response = Rejected(request, validationError);
            }
            else if (!_replayGuard.TryAccept(request.RequestId, now))
            {
                response = Rejected(request, "duplicate-request");
            }
            else
            {
                response = await _handler(request, deadline.Token).ConfigureAwait(false);
                _diagnostic?.Invoke("control-handler-returned");
            }

            await MaintenanceControlFraming.WriteResponseAsync(pipe, response, deadline.Token).ConfigureAwait(false);
            _diagnostic?.Invoke("control-response-written");
        }
        catch (OperationCanceledException) when (serviceCancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or OperationCanceledException)
        {
            // A malformed, truncated, or timed-out client is isolated to this one connection.
            _diagnostic?.Invoke($"control-connection-failed type={exception.GetType().Name}");
        }
    }

    private static MaintenanceControlResponse Rejected(MaintenanceControlRequest request, string diagnostic) => new(
        MaintenanceControlResponse.CurrentVersion,
        request.RequestId,
        MaintenanceControlStatus.Rejected,
        null,
        diagnostic);
}

internal static class MaintenancePipeAuthorization
{
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdministratorsSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier NetworkSid = new(WellKnownSidType.NetworkSid, null);

    public static bool IsAuthorized(NamedPipeServerStream pipe)
    {
        try
        {
            var authorized = false;
            pipe.RunAsClient(() =>
            {
                using var identity = WindowsIdentity.GetCurrent(true);
                authorized = identity is not null && IsAuthorized(identity);
            });
            return authorized;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsAuthorized(WindowsIdentity identity)
    {
        if (identity.User is null || identity.Groups?.Contains(NetworkSid) == true)
        {
            return false;
        }

        return identity.User.Equals(SystemSid) || new WindowsPrincipal(identity).IsInRole(AdministratorsSid);
    }
}

internal static class MaintenancePipeFactory
{
    private const string PipePath = @"\\.\pipe\Quail.Maintenance.v1";
    private const string PipeSddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)";
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagFirstPipeInstance = 0x00080000;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeTypeByte = 0x00000000;
    private const uint PipeReadModeByte = 0x00000000;
    private const uint PipeWait = 0x00000000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private const uint MaximumInstances = 1;
    private const uint BufferBytes = MaintenanceControlValidation.MaximumFrameBytes + sizeof(int);

    public static NamedPipeServerStream CreateServer()
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(PipeSddl, 1, out var descriptor, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create the maintenance pipe security descriptor.");
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = false
            };
            var handle = CreateNamedPipeW(
                PipePath,
                PipeAccessDuplex | FileFlagFirstPipeInstance | FileFlagOverlapped,
                PipeTypeByte | PipeReadModeByte | PipeWait | PipeRejectRemoteClients,
                MaximumInstances,
                BufferBytes,
                BufferBytes,
                0,
                ref attributes);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "Could not create the maintenance control pipe.");
            }

            return new NamedPipeServerStream(PipeDirection.InOut, isAsync: true, isConnected: false, handle);
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafePipeHandle CreateNamedPipeW(
        string name,
        uint openMode,
        uint pipeMode,
        uint maximumInstances,
        uint outputBufferSize,
        uint inputBufferSize,
        uint defaultTimeoutMilliseconds,
        ref SecurityAttributes securityAttributes);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor,
        uint stringSdRevision,
        out nint securityDescriptor,
        out uint securityDescriptorSize);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
