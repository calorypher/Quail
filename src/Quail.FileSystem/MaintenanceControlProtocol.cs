using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quail.FileSystem;

public static class MaintenanceControlValidation
{
    public const int MaximumFrameBytes = 16 * 1024;
    public const int MaximumDiagnosticLength = 512;
    public static readonly TimeSpan MaximumClockSkew = TimeSpan.FromMinutes(5);

    public static string? Validate(MaintenanceControlRequest request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version != MaintenanceControlRequest.CurrentVersion)
        {
            return "unsupported-version";
        }

        if (request.RequestId == Guid.Empty)
        {
            return "missing-request-id";
        }

        if ((request.IssuedUtc - now).Duration() > MaximumClockSkew)
        {
            return "stale-request";
        }

        if (!Enum.IsDefined(request.Command))
        {
            return "unknown-command";
        }

        if (request.Command == MaintenanceControlCommand.GetOperationStatus)
        {
            return request.OperationId is null || request.OperationId == Guid.Empty
                ? "missing-operation-id"
                : request.VolumeIdentity is not null
                    ? "unexpected-volume-identity"
                    : null;
        }

        if (request.OperationId is not null)
        {
            return "unexpected-operation-id";
        }

        return IsCanonicalVolumeIdentity(request.VolumeIdentity)
            ? null
            : "invalid-volume-identity";
    }

    public static bool IsCanonicalVolumeIdentity(string? value)
    {
        const string prefix = @"\\?\Volume{";
        if (value is null || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !value.EndsWith('}') || value.Length != prefix.Length + 37)
        {
            return false;
        }

        return Guid.TryParseExact(value.AsSpan(prefix.Length, 36), "D", out _);
    }

    public static string? BoundDiagnostic(string? diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return null;
        }

        var singleLine = diagnostic.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= MaximumDiagnosticLength
            ? singleLine
            : singleLine[..MaximumDiagnosticLength];
    }
}

public static class MaintenanceControlFraming
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() }
    };

    public static Task<MaintenanceControlRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken) =>
        ReadAsync<MaintenanceControlRequest>(stream, cancellationToken);

    public static Task<MaintenanceControlResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken) =>
        ReadAsync<MaintenanceControlResponse>(stream, cancellationToken);

    public static Task WriteRequestAsync(Stream stream, MaintenanceControlRequest request, CancellationToken cancellationToken) =>
        WriteAsync(stream, request, cancellationToken);

    public static Task WriteResponseAsync(Stream stream, MaintenanceControlResponse response, CancellationToken cancellationToken) =>
        WriteAsync(stream, response with { Diagnostic = MaintenanceControlValidation.BoundDiagnostic(response.Diagnostic) }, cancellationToken);

    private static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        await ReadExactlyAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaintenanceControlValidation.MaximumFrameBytes)
        {
            throw new InvalidDataException("Control frame length is invalid.");
        }

        var payload = new byte[length];
        await ReadExactlyAsync(stream, payload, cancellationToken).ConfigureAwait(false);
        RejectDuplicateProperties(payload);
        try
        {
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions)
                ?? throw new InvalidDataException("Control frame is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Control frame JSON is invalid.", exception);
        }
    }

    private static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions);
        if (payload.Length is <= 0 or > MaintenanceControlValidation.MaximumFrameBytes)
        {
            throw new InvalidDataException("Control frame exceeds the size limit.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(Stream stream, Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("Control frame ended early.");
            }

            offset += read;
        }
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Control frame must be a JSON object.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException("Control frame contains a duplicate property.");
            }
        }
    }
}

internal sealed class MaintenanceRequestReplayGuard
{
    private const int MaximumRememberedRequests = 1024;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, DateTimeOffset> _seen = [];

    public bool TryAccept(Guid requestId, DateTimeOffset now)
    {
        lock (_gate)
        {
            var cutoff = now - MaintenanceControlValidation.MaximumClockSkew;
            foreach (var expired in _seen.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray())
            {
                _seen.Remove(expired);
            }

            if (_seen.ContainsKey(requestId))
            {
                return false;
            }

            if (_seen.Count >= MaximumRememberedRequests)
            {
                var oldest = _seen.MinBy(pair => pair.Value).Key;
                _seen.Remove(oldest);
            }

            _seen.Add(requestId, now);
            return true;
        }
    }
}
