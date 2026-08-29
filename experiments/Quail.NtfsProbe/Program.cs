using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Text;

return await Probe.RunAsync(args);

internal static class Probe
{
    private const uint ErrorHandleEof = 38;
    private const uint ErrorJournalEntryDeleted = 1181;
    private const ushort MinimumSupportedReadRecordVersion = 2;
    private const ushort MaximumSupportedReadRecordVersion = 3;

    public static Task<int> RunAsync(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return Task.FromResult(1);
            }

            var command = args[0].ToLowerInvariant();
            var options = Options.Parse(args[1..]);
            return Task.FromResult(command switch
            {
                "volumes" => ListVolumes(),
                "journal" => PrintJournal(options.Volume),
                "enumerate" => Enumerate(options.Volume),
                "read" => ReadJournal(options.Volume, options.StartUsn, options.JournalId),
                "checkpoint" => PrintCheckpoint(options.Volume),
                "namespace" => ValidateNamespace(options.Volume, options.RootName),
                _ => UsageError($"Unknown command '{command}'."),
            });
        }
        catch (Win32Exception exception)
        {
            Console.Error.WriteLine($"WIN32_ERROR={exception.NativeErrorCode} MESSAGE={exception.Message}");
            return Task.FromResult(2);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"ERROR={exception.GetType().Name} MESSAGE={exception.Message}");
            return Task.FromResult(3);
        }
    }

    private static int ListVolumes()
    {
        foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Console.WriteLine($"VOLUME drive={drive.Name} type={drive.DriveType} ready={drive.IsReady} format={(drive.IsReady ? drive.DriveFormat : "n/a")} label={(drive.IsReady ? drive.VolumeLabel : "n/a")}");
            }
            catch (IOException)
            {
                Console.WriteLine($"VOLUME drive={drive.Name} type={drive.DriveType} ready=false format=n/a label=n/a");
            }
        }

        return 0;
    }

    private static int PrintJournal(string volume)
    {
        using var handle = OpenVolume(volume);
        var journal = QueryJournal(handle);
        Console.WriteLine($"JOURNAL volume={NormalizeVolume(volume)} journalId=0x{journal.UsnJournalId:x16} firstUsn={journal.FirstUsn} nextUsn={journal.NextUsn} lowestValidUsn={journal.LowestValidUsn} maxUsn={journal.MaxUsn} maximumSize={journal.MaximumSize} allocationDelta={journal.AllocationDelta} minMajor={journal.MinSupportedMajorVersion} maxMajor={journal.MaxSupportedMajorVersion}");
        return 0;
    }

    private static int PrintCheckpoint(string volume)
    {
        using var handle = OpenVolume(volume);
        var journal = QueryJournal(handle);
        Console.WriteLine($"CHECKPOINT volume={NormalizeVolume(volume)} journalId=0x{journal.UsnJournalId:x16} nextUsn={journal.NextUsn} firstUsn={journal.FirstUsn} lowestValidUsn={journal.LowestValidUsn}");
        return 0;
    }

    private static int Enumerate(string volume)
    {
        using var handle = OpenVolume(volume);
        var result = ReadEnumeration(handle, printRecords: false);
        Console.WriteLine($"ENUMERATION volume={NormalizeVolume(volume)} records={result.Records.Count} parseErrors={result.ParseErrors} unsupportedRecords={result.UnsupportedRecords} elapsedMs={result.Elapsed.TotalMilliseconds:F3} recordsPerSecond={Rate(result.Records.Count, result.Elapsed):F0} cpuMs={result.CpuTime.TotalMilliseconds:F3} peakWorkingSetBytes={Process.GetCurrentProcess().PeakWorkingSet64}");
        return result.UnsupportedRecords == 0 ? 0 : 4;
    }

    private static int ValidateNamespace(string volume, string rootName)
    {
        if (string.IsNullOrWhiteSpace(rootName))
        {
            return UsageError("namespace requires --root-name <directory-name>.");
        }

        using var handle = OpenVolume(volume);
        var result = ReadEnumeration(handle, printRecords: false);
        var roots = result.Records.Where(record => string.Equals(record.Name, rootName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (roots.Count == 0)
        {
            throw new InvalidOperationException($"No enumerated record named '{rootName}' was found.");
        }

        var byFrn = result.Records.GroupBy(record => record.FileReferenceNumber).ToDictionary(group => group.Key, group => group.First());
        foreach (var root in roots)
        {
            Console.WriteLine($"NAMESPACE_ROOT frn={root.FileReferenceNumber} parentFrn={root.ParentFileReferenceNumber} name={Escape(root.Name)} attributes=0x{root.FileAttributes:x8}");
            foreach (var record in result.Records.Where(record => IsDescendantOf(record, root.FileReferenceNumber, byFrn)).OrderBy(record => ReconstructPath(record, byFrn), StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"PATH frn={record.FileReferenceNumber} parentFrn={record.ParentFileReferenceNumber} path={Escape(ReconstructPath(record, byFrn))} attributes=0x{record.FileAttributes:x8} usn={record.Usn}");
            }
        }

        Console.WriteLine($"NAMESPACE volume={NormalizeVolume(volume)} records={result.Records.Count} parseErrors={result.ParseErrors} unsupportedRecords={result.UnsupportedRecords}");
        return result.UnsupportedRecords == 0 ? 0 : 4;
    }

    private static bool IsDescendantOf(UsnRecord record, string rootFrn, IReadOnlyDictionary<string, UsnRecord> byFrn)
    {
        var current = record;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current.FileReferenceNumber))
        {
            if (current.FileReferenceNumber == rootFrn)
            {
                return true;
            }

            if (!byFrn.TryGetValue(current.ParentFileReferenceNumber, out current!))
            {
                return false;
            }
        }

        return false;
    }

    private static string ReconstructPath(UsnRecord record, IReadOnlyDictionary<string, UsnRecord> byFrn)
    {
        var names = new List<string>();
        var current = record;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (visited.Add(current.FileReferenceNumber))
        {
            names.Add(current.Name);
            if (!byFrn.TryGetValue(current.ParentFileReferenceNumber, out current!))
            {
                break;
            }
        }

        names.Reverse();
        return string.Join("\\", names);
    }

    private static int ReadJournal(string volume, long? startUsn, ulong? expectedJournalId)
    {
        using var handle = OpenVolume(volume);
        var journal = QueryJournal(handle);
        var requestedUsn = startUsn ?? journal.FirstUsn;
        if (expectedJournalId is { } expected && expected != journal.UsnJournalId)
        {
            Console.WriteLine($"CONTINUITY status=rebuild-required reason=journal-id-mismatch savedJournalId=0x{expected:x16} currentJournalId=0x{journal.UsnJournalId:x16} savedUsn={requestedUsn}");
            return 0;
        }

        if (requestedUsn < journal.FirstUsn || requestedUsn < journal.LowestValidUsn)
        {
            Console.WriteLine($"CONTINUITY status=rebuild-required reason=saved-usn-before-readable-range savedUsn={requestedUsn} firstUsn={journal.FirstUsn} lowestValidUsn={journal.LowestValidUsn}");
            return 0;
        }

        var buffer = new byte[1024 * 1024];
        var input = new ReadUsnJournalDataV1(requestedUsn, uint.MaxValue, 0, 0, 0, journal.UsnJournalId, MinimumSupportedReadRecordVersion, MaximumSupportedReadRecordVersion);
        try
        {
            var records = 0;
            long nextUsn = requestedUsn;
            while (true)
            {
                var bytesReturned = DeviceIoControlStruct(handle, Native.FsctlReadUsnJournal, input, buffer);
                if (bytesReturned < sizeof(long))
                {
                    throw new InvalidOperationException("FSCTL_READ_USN_JOURNAL returned fewer than eight bytes.");
                }

                nextUsn = BitConverter.ToInt64(buffer, 0);
                records += ParseUsnRecords(buffer, sizeof(long), bytesReturned, record => Console.WriteLine($"USN_RECORD major={record.MajorVersion} frn={record.FileReferenceNumber} parentFrn={record.ParentFileReferenceNumber} usn={record.Usn} reason=0x{record.Reason:x8} attributes=0x{record.FileAttributes:x8} name={Escape(record.Name)}"), failOnInvalidRecord: true);
                if (bytesReturned == sizeof(long))
                {
                    break;
                }

                input = input with { StartUsn = nextUsn };
            }

            Console.WriteLine($"CATCH_UP status=ok volume={NormalizeVolume(volume)} startUsn={requestedUsn} nextUsn={nextUsn} records={records} requestedMinMajor={MinimumSupportedReadRecordVersion} requestedMaxMajor={MaximumSupportedReadRecordVersion}");
            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorJournalEntryDeleted)
        {
            Console.WriteLine($"CONTINUITY status=rebuild-required reason=journal-entry-deleted savedUsn={requestedUsn} firstUsn={journal.FirstUsn} lowestValidUsn={journal.LowestValidUsn}");
            return 0;
        }
    }

    private static EnumerationResult ReadEnumeration(SafeFileHandle handle, bool printRecords)
    {
        var stopwatch = Stopwatch.StartNew();
        var process = Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var journal = QueryJournal(handle);
        var input = new MftEnumDataV0(0, 0, journal.NextUsn);
        var buffer = new byte[1024 * 1024];
        var records = new List<UsnRecord>();
        var parseErrors = 0;
        var unsupported = 0;

        while (true)
        {
            uint bytesReturned;
            try
            {
                bytesReturned = DeviceIoControlStruct(handle, Native.FsctlEnumUsnData, input, buffer);
            }
            catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorHandleEof)
            {
                break;
            }

            if (bytesReturned < sizeof(long))
            {
                throw new InvalidOperationException("FSCTL_ENUM_USN_DATA returned fewer than eight bytes.");
            }

            input = input with { StartFileReferenceNumber = BitConverter.ToUInt64(buffer, 0) };
            ParseUsnRecords(buffer, sizeof(long), bytesReturned, record =>
            {
                records.Add(record);
                if (printRecords)
                {
                    Console.WriteLine($"MFT_RECORD major={record.MajorVersion} frn={record.FileReferenceNumber} parentFrn={record.ParentFileReferenceNumber} usn={record.Usn} attributes=0x{record.FileAttributes:x8} name={Escape(record.Name)}");
                }
            }, () => unsupported++, () => parseErrors++);
        }

        stopwatch.Stop();
        return new EnumerationResult(records, parseErrors, unsupported, stopwatch.Elapsed, process.TotalProcessorTime - cpuStart);
    }

    private static int ParseUsnRecords(byte[] buffer, int offset, uint bytesReturned, Action<UsnRecord> onRecord, Action? onUnsupported = null, Action? onParseError = null, bool failOnInvalidRecord = false)
    {
        var count = 0;
        var end = checked((int)bytesReturned);
        while (offset < end)
        {
            if (end - offset < 8)
            {
                onParseError?.Invoke();
                if (failOnInvalidRecord)
                {
                    throw new InvalidOperationException("Malformed USN record: fewer than eight bytes remain.");
                }

                break;
            }

            var length = BitConverter.ToInt32(buffer, offset);
            var major = BitConverter.ToUInt16(buffer, offset + 4);
            if (length < 8 || length > end - offset)
            {
                onParseError?.Invoke();
                if (failOnInvalidRecord)
                {
                    throw new InvalidOperationException($"Malformed USN record: invalid length {length} for remaining buffer size {end - offset}.");
                }

                break;
            }

            if (major is not (2 or 3))
            {
                Console.Error.WriteLine($"UNSUPPORTED_RECORD major={major} length={length}");
                onUnsupported?.Invoke();
                if (failOnInvalidRecord)
                {
                    throw new NotSupportedException($"Unsupported USN record major version {major}.");
                }

                offset += length;
                continue;
            }

            var headerSize = major == 2 ? 60 : 76;
            if (length < headerSize)
            {
                onParseError?.Invoke();
                if (failOnInvalidRecord)
                {
                    throw new InvalidOperationException($"Malformed USN record v{major}: length {length} is smaller than header size {headerSize}.");
                }

                offset += length;
                continue;
            }

            var nameLength = BitConverter.ToUInt16(buffer, offset + (major == 2 ? 56 : 72));
            var nameOffset = BitConverter.ToUInt16(buffer, offset + (major == 2 ? 58 : 74));
            if (nameOffset + nameLength > length || nameLength % 2 != 0)
            {
                onParseError?.Invoke();
                if (failOnInvalidRecord)
                {
                    throw new InvalidOperationException($"Malformed USN record v{major}: invalid name offset {nameOffset} or length {nameLength}.");
                }

                offset += length;
                continue;
            }

            var record = new UsnRecord(
                major,
                FileId(buffer, offset + 8, major == 2 ? 8 : 16),
                FileId(buffer, offset + (major == 2 ? 16 : 24), major == 2 ? 8 : 16),
                BitConverter.ToInt64(buffer, offset + (major == 2 ? 24 : 40)),
                BitConverter.ToUInt32(buffer, offset + (major == 2 ? 40 : 56)),
                BitConverter.ToUInt32(buffer, offset + (major == 2 ? 52 : 68)),
                Encoding.Unicode.GetString(buffer, offset + nameOffset, nameLength));
            onRecord(record);
            count++;
            offset += length;
        }

        return count;
    }

    private static SafeFileHandle OpenVolume(string volume)
    {
        var normalized = NormalizeVolume(volume);
        var handle = Native.CreateFile($@"\\.\{normalized}", Native.GenericRead, Native.FileShareRead | Native.FileShareWrite | Native.FileShareDelete, IntPtr.Zero, Native.OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"CreateFile failed for {normalized}");
        }

        return handle;
    }

    private static UsnJournalDataV1 QueryJournal(SafeFileHandle handle)
    {
        var buffer = new byte[Marshal.SizeOf<UsnJournalDataV1>()];
        var bytesReturned = DeviceIoControlBuffer(handle, Native.FsctlQueryUsnJournal, buffer);
        if (bytesReturned < Marshal.SizeOf<UsnJournalDataV1>())
        {
            throw new InvalidOperationException($"FSCTL_QUERY_USN_JOURNAL returned {bytesReturned} bytes, expected at least {Marshal.SizeOf<UsnJournalDataV1>()}.");
        }

        return MemoryMarshal.Read<UsnJournalDataV1>(buffer);
    }

    private static uint DeviceIoControlBuffer(SafeFileHandle handle, uint controlCode, byte[] output)
    {
        if (!Native.DeviceIoControl(handle, controlCode, IntPtr.Zero, 0, output, checked((uint)output.Length), out var bytesReturned, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        return bytesReturned;
    }

    private static uint DeviceIoControlStruct<T>(SafeFileHandle handle, uint controlCode, T input, byte[] output) where T : struct
    {
        var inputSize = Marshal.SizeOf<T>();
        var inputBuffer = Marshal.AllocHGlobal(inputSize);
        try
        {
            Marshal.StructureToPtr(input, inputBuffer, false);
            if (!Native.DeviceIoControl(handle, controlCode, inputBuffer, checked((uint)inputSize), output, checked((uint)output.Length), out var bytesReturned, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return bytesReturned;
        }
        finally
        {
            Marshal.FreeHGlobal(inputBuffer);
        }
    }

    private static string NormalizeVolume(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 1 || !char.IsLetter(value[0]))
        {
            throw new ArgumentException("A drive letter such as C: is required.", nameof(value));
        }

        return char.ToUpperInvariant(value[0]) + ":";
    }

    private static string FileId(byte[] buffer, int offset, int length)
    {
        var low = BitConverter.ToUInt64(buffer, offset);
        return length == 8
            ? $"0x{low:x16}"
            : $"0x{BitConverter.ToUInt64(buffer, offset + 8):x16}{low:x16}";
    }

    private static double Rate(int records, TimeSpan elapsed) => elapsed.TotalSeconds <= 0 ? 0 : records / elapsed.TotalSeconds;
    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    private static int UsageError(string message) { Console.Error.WriteLine(message); PrintUsage(); return 1; }
    private static void PrintUsage() => Console.Error.WriteLine("Usage: Quail.NtfsProbe <volumes|journal|enumerate|read|checkpoint|namespace> --volume <C:> [--start-usn <number>] [--journal-id <hex>] [--root-name <name>]");

    private sealed record Options(string Volume, long? StartUsn, ulong? JournalId, string RootName)
    {
        public static Options Parse(string[] args)
        {
            string? volume = null;
            long? startUsn = null;
            ulong? journalId = null;
            string? rootName = null;
            for (var index = 0; index < args.Length; index += 2)
            {
                if (index + 1 >= args.Length) throw new ArgumentException($"Missing value for {args[index]}.");
                switch (args[index])
                {
                    case "--volume": volume = args[index + 1]; break;
                    case "--start-usn": startUsn = long.Parse(args[index + 1], System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--journal-id": journalId = Convert.ToUInt64(args[index + 1].Replace("0x", "", StringComparison.OrdinalIgnoreCase), 16); break;
                    case "--root-name": rootName = args[index + 1]; break;
                    default: throw new ArgumentException($"Unknown option {args[index]}.");
                }
            }

            return new Options(volume ?? "", startUsn, journalId, rootName ?? "");
        }
    }

    private sealed record EnumerationResult(IReadOnlyList<UsnRecord> Records, int ParseErrors, int UnsupportedRecords, TimeSpan Elapsed, TimeSpan CpuTime);
    private sealed record UsnRecord(ushort MajorVersion, string FileReferenceNumber, string ParentFileReferenceNumber, long Usn, uint Reason, uint FileAttributes, string Name);

    [StructLayout(LayoutKind.Sequential)]
    private struct UsnJournalDataV1
    {
        public ulong UsnJournalId;
        public long FirstUsn;
        public long NextUsn;
        public long LowestValidUsn;
        public long MaxUsn;
        public ulong MaximumSize;
        public ulong AllocationDelta;
        public ushort MinSupportedMajorVersion;
        public ushort MaxSupportedMajorVersion;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct MftEnumDataV0(ulong StartFileReferenceNumber, long LowUsn, long HighUsn);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ReadUsnJournalDataV1(long StartUsn, uint ReasonMask, uint ReturnOnlyOnClose, ulong Timeout, ulong BytesToWaitFor, ulong UsnJournalId, ushort MinMajorVersion, ushort MaxMajorVersion);

    private static class Native
    {
        public const uint GenericRead = 0x80000000;
        public const uint FileShareRead = 0x00000001;
        public const uint FileShareWrite = 0x00000002;
        public const uint FileShareDelete = 0x00000004;
        public const uint OpenExisting = 3;
        public const uint FsctlEnumUsnData = 0x000900B3;
        public const uint FsctlReadUsnJournal = 0x000900BB;
        public const uint FsctlQueryUsnJournal = 0x000900F4;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, IntPtr inputBuffer, uint inputBufferSize, [Out] byte[] outputBuffer, uint outputBufferSize, out uint bytesReturned, IntPtr overlapped);
    }
}
