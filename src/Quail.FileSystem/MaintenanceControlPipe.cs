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
            TokenImpersonationLevel.Impersonation);
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
            var authorized = MaintenancePipeAuthorization.IsAuthorized(pipe, out var authorizationDiagnostic);
            _diagnostic?.Invoke(authorized
                ? $"control-authorized {authorizationDiagnostic}"
                : $"control-unauthorized {authorizationDiagnostic}");
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
    private const int NetworkSidType = 9;
    private const int LocalSystemSidType = 22;
    private const int BuiltinAdministratorsSidType = 26;
    private const int MaximumSidBytes = 68;

    public static bool IsAuthorized(NamedPipeServerStream pipe, out string diagnostic)
    {
        var authorizationDiagnostic = "before-impersonation";
        try
        {
            var authorized = false;
            pipe.RunAsClient(() =>
            {
                authorizationDiagnostic = "before-membership-check";
                (authorized, authorizationDiagnostic) = AuthorizeCurrentToken();
            });
            diagnostic = authorizationDiagnostic;
            return authorized;
        }
        catch (Exception exception)
        {
            var nativeError = exception is Win32Exception win32
                ? $"-native-{win32.NativeErrorCode}"
                : string.Empty;
            diagnostic = $"{authorizationDiagnostic}-error-{exception.GetType().Name}{nativeError}";
            return false;
        }
    }

    internal static (bool Authorized, string Diagnostic) AuthorizeMembership(
        bool isSystem,
        bool isNetwork,
        bool isAdministrator)
    {
        if (isSystem)
        {
            return (true, "system");
        }

        if (isNetwork)
        {
            return (false, "network-token");
        }

        return isAdministrator
            ? (true, "administrator")
            : (false, "not-administrator");
    }

    private static (bool Authorized, string Diagnostic) AuthorizeCurrentToken()
    {
        return AuthorizeMembership(
            IsCurrentTokenMember(LocalSystemSidType),
            IsCurrentTokenMember(NetworkSidType),
            IsCurrentTokenMember(BuiltinAdministratorsSidType));
    }

    private static bool IsCurrentTokenMember(int wellKnownSidType)
    {
        var sid = new byte[MaximumSidBytes];
        var sidBytes = (uint)sid.Length;
        if (!CreateWellKnownSid(wellKnownSidType, nint.Zero, sid, ref sidBytes))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not create a well-known SID for control authorization.");
        }

        if (!CheckTokenMembership(nint.Zero, sid, out var isMember))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not inspect the control client token.");
        }

        return isMember;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateWellKnownSid(
        int wellKnownSidType,
        nint domainSid,
        byte[] sid,
        ref uint sidBytes);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CheckTokenMembership(
        nint tokenHandle,
        byte[] sidToCheck,
        [MarshalAs(UnmanagedType.Bool)] out bool isMember);
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
