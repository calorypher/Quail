using System.Globalization;
using System.Reflection;
using Quail.FileSystem;

public sealed class CliApplication
{
    public const int SuccessExitCode = 0;
    public const int OperationalErrorExitCode = 1;
    public const int InputErrorExitCode = 2;

    private readonly TextWriter _output;
    private readonly TextWriter _error;
    private readonly IndexedEntryOpener _opener;

    public CliApplication(TextWriter output, TextWriter error, IndexedEntryOpener? opener = null)
    {
        _output = output;
        _error = error;
        _opener = opener ?? new IndexedEntryOpener();
    }

    public int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
            {
                Help();
                return 0;
            }

            if (args[0] is "version" or "--version")
            {
                _output.WriteLine($"quail {ProductVersion}");
                return 0;
            }

            if (args.Length > 1 && args[1] is "--help" or "-h")
            {
                Help();
                return 0;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "build":
                case "rebuild":
                    Build(args);
                    break;
                case "sync":
                    Sync(args);
                    break;
                case "status":
                    Status(args);
                    break;
                case "search":
                    Search(args);
                    break;
                case "open":
                    Open(args);
                    break;
                default:
                    throw new ArgumentException($"Unknown command '{args[0]}'.");
            }

            return 0;
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            _error.WriteLine($"ERROR {e.Message}");
            return 2;
        }
        catch (Exception e)
        {
            _error.WriteLine($"ERROR {e.Message}");
            return 1;
        }
    }

    private void Build(string[] a)
    {
        var (i, v) = IndexVolume(a);
        var m = new IndexStore(i).Build(v);
        _output.WriteLine(
            $"BUILD source={Path.GetFullPath(i)} state=complete " +
            $"records={m.RecordCount} elapsedMs={m.Elapsed.TotalMilliseconds:F0}");
    }

    private void Sync(string[] a)
    {
        var (i, v) = IndexVolume(a);
        var s = new IndexStore(i).Sync(v);
        _output.WriteLine(
            $"SYNC source={Path.GetFullPath(i)} " +
            $"state={(s.RebuildRequired ? "rebuild-required" : "complete")} " +
            $"recordsApplied={s.AppliedRecords} reason={s.Reason ?? "none"}");
    }

    private void Status(string[] a)
    {
        foreach (var i in Indexes(a, "status"))
        {
            var s = new IndexStore(i).GetStatus();
            _output.WriteLine(
                $"STATUS source={Path.GetFullPath(i)} " +
                $"state={(s.State == IndexState.RebuildRequired ? "rebuild-required" : s.State.ToString().ToLowerInvariant())} " +
                $"volumeIdentity={s.VolumeIdentity ?? "n/a"} mountPoint={s.MountPoint ?? "n/a"} " +
                $"records={s.RecordCount} completedUtc={s.CompletedUtc?.ToString("O") ?? "n/a"} " +
                $"lastRefreshedUtc={s.LastRefreshedUtc?.ToString("O") ?? "n/a"} " +
                $"journalId={s.Checkpoint?.JournalId.ToString("X16") ?? "n/a"} " +
                $"nextUsn={s.Checkpoint?.NextUsn.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
                $"firstUsn={s.Checkpoint?.FirstUsn.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
                $"lowestValidUsn={s.Checkpoint?.LowestValidUsn.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
                $"detail={s.Detail ?? "none"}");
        }
    }

    private void Search(string[] a)
    {
        var (ix, q) = Query(a);
        var r = MultiIndexSearch.Search(ix.Select(x => new IndexStore(x)), q);
        _output.WriteLine($"SEARCH indexes={ix.Count} results={r.Count} limit={q.Limit}");

        foreach (var x in r)
        {
            _output.WriteLine(
                $"RESULT source={x.SourceIdentity} type={(x.Result.IsDirectory ? "dir" : "file")} " +
                $"fileId={x.Result.FileId} name={x.Result.Name} extension={x.Result.Extension ?? "n/a"} " +
                $"size={x.Result.LogicalSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
                $"modifiedUtcFileTime={x.Result.LastWriteTimeUtcFileTime?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
                $"path={x.Result.FullPath ?? "n/a"}");
        }
    }

    private void Open(string[] a)
    {
        var d = Options(a, "open", new[] { "--index", "--file-id" });
        if (!d.TryGetValue("--index", out var i) || !d.TryGetValue("--file-id", out var id))
        {
            throw new ArgumentException("open requires --index <database-path> and --file-id <hex-file-id>.");
        }

        _opener.Open(new IndexStore(i), new NativeFileId(Convert.FromHexString(id)));
        _output.WriteLine($"OPEN source={Path.GetFullPath(i)} fileId={id.ToUpperInvariant()} state=launched");
    }

    private static (string, string) IndexVolume(string[] a)
    {
        var d = Options(a, a[0], new[] { "--index", "--volume" });
        if (!d.TryGetValue("--index", out var i) || !d.TryGetValue("--volume", out var v))
        {
            throw new ArgumentException(
                $"{a[0]} requires --index <database-path> and --volume <mount-point>.");
        }

        return (i, v);
    }

    private static IReadOnlyList<string> Indexes(string[] a, string c)
    {
        var r = new List<string>();
        for (int p = 1; p < a.Length; p++)
        {
            if (a[p] != "--index")
            {
                throw new ArgumentException($"Unknown {c} option '{a[p]}'.");
            }

            r.Add(Value(a, ref p, "--index"));
        }

        if (r.Count == 0)
        {
            throw new ArgumentException($"{c} requires --index <database-path>.");
        }

        return r;
    }

    private static (IReadOnlyList<string>, FileSearchQuery) Query(string[] a)
    {
        var ix = new List<string>();
        string? n = null;
        string? e = null;
        var t = SearchEntryType.Any;
        int l = 50;
        long? min = null;
        long? max = null;
        long? after = null;
        long? before = null;
        bool h = false;
        bool r = false;
        bool s = false;
        bool typeSet = false;
        bool limitSet = false;

        for (int p = 1; p < a.Length; p++)
        {
            switch (a[p])
            {
                case "--index":
                    ix.Add(Value(a, ref p, "--index"));
                    break;
                case "--type":
                    if (typeSet)
                    {
                        throw new ArgumentException("--type may be specified only once.");
                    }

                    typeSet = true;
                    t = Value(a, ref p, "--type") switch
                    {
                        "any" => SearchEntryType.Any,
                        "file" => SearchEntryType.File,
                        "dir" => SearchEntryType.Directory,
                        _ => throw new ArgumentException("--type must be file, dir, or any.")
                    };
                    break;
                case "--ext":
                    e = Once(e, Value(a, ref p, "--ext"), "--ext");
                    break;
                case "--limit":
                    if (limitSet)
                    {
                        throw new ArgumentException("--limit may be specified only once.");
                    }

                    limitSet = true;
                    if (!int.TryParse(
                        Value(a, ref p, "--limit"),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out l))
                    {
                        throw new ArgumentException("--limit must be a whole number.");
                    }

                    break;
                case "--min-size":
                    if (min is not null)
                    {
                        throw new ArgumentException("--min-size may be specified only once.");
                    }

                    min = Number(Value(a, ref p, "--min-size"), "--min-size");
                    break;
                case "--max-size":
                    if (max is not null)
                    {
                        throw new ArgumentException("--max-size may be specified only once.");
                    }

                    max = Number(Value(a, ref p, "--max-size"), "--max-size");
                    break;
                case "--modified-after":
                    if (after is not null)
                    {
                        throw new ArgumentException("--modified-after may be specified only once.");
                    }

                    after = Time(Value(a, ref p, "--modified-after"), "--modified-after");
                    break;
                case "--modified-before":
                    if (before is not null)
                    {
                        throw new ArgumentException("--modified-before may be specified only once.");
                    }

                    before = Time(Value(a, ref p, "--modified-before"), "--modified-before");
                    break;
                case "--hidden":
                    if (h)
                    {
                        throw new ArgumentException("--hidden may be specified only once.");
                    }

                    h = true;
                    break;
                case "--read-only":
                    if (r)
                    {
                        throw new ArgumentException("--read-only may be specified only once.");
                    }

                    r = true;
                    break;
                case "--system":
                    if (s)
                    {
                        throw new ArgumentException("--system may be specified only once.");
                    }

                    s = true;
                    break;
                default:
                    if (a[p].StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown search option '{a[p]}'.");
                    }

                    n = Once(n, a[p], "search query");
                    break;
            }
        }

        if (ix.Count == 0 || n is null)
        {
            throw new ArgumentException(
                "search requires one or more --index <database-path> values and <query>.");
        }

        return (ix, new FileSearchQuery(n, t, e, l, min, max, after, before, h, r, s));
    }

    private static Dictionary<string, string> Options(string[] a, string c, string[] allowed)
    {
        var d = new Dictionary<string, string>();
        for (int p = 1; p < a.Length; p++)
        {
            if (!allowed.Contains(a[p]))
            {
                throw new ArgumentException($"Unknown {c} option '{a[p]}'.");
            }

            if (!d.TryAdd(a[p], Value(a, ref p, a[p])))
            {
                throw new ArgumentException($"{a[p]} may be specified only once.");
            }
        }

        return d;
    }

    private static string Value(string[] a, ref int p, string o)
    {
        if (++p >= a.Length || a[p].StartsWith("--"))
        {
            throw new ArgumentException($"{o} requires a value.");
        }

        return a[p];
    }

    private static string Once(string? old, string value, string o)
    {
        if (old is not null)
        {
            throw new ArgumentException($"{o} may be specified only once.");
        }

        return value;
    }

    private static long Number(string v, string o)
    {
        if (!long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var x))
        {
            throw new ArgumentException($"{o} must be a non-negative whole number of bytes.");
        }

        return x;
    }

    private static long Time(string v, string o)
    {
        if (!DateTimeOffset.TryParse(
                v,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var x) ||
            !(v.EndsWith("Z", StringComparison.OrdinalIgnoreCase) ||
              (v.Length >= 6 && (v[^6] == '+' || v[^6] == '-') && v[^3] == ':')))
        {
            throw new ArgumentException(
                $"{o} must be an ISO-8601 timestamp with Z or a numeric UTC offset.");
        }

        return x.UtcDateTime.ToFileTimeUtc();
    }

    private void Help()
    {
        _output.WriteLine("Usage: quail <command> [options]");
        _output.WriteLine("Commands: build, rebuild, sync, status, search, open, help, version");
        _output.WriteLine("build|rebuild --index <database-path> --volume <mount-point>");
        _output.WriteLine("sync --index <database-path> --volume <mount-point>");
        _output.WriteLine("status --index <database-path> [--index <database-path> ...]");
        _output.WriteLine(
            "search --index <database-path> [--index <database-path> ...] <query> " +
            "[--type any|file|dir] [--ext <extension>] [--limit 1..1000] " +
            "[--min-size <bytes>] [--max-size <bytes>] " +
            "[--modified-after <ISO-8601>] [--modified-before <ISO-8601>] " +
            "[--hidden] [--read-only] [--system]");
        _output.WriteLine("open --index <database-path> --file-id <hex-file-id>");
        _output.WriteLine("Exit codes: 0 success, 2 invalid input, 1 operational failure.");
    }

    private static string ProductVersion
    {
        get
        {
            var informationalVersion = typeof(CliApplication).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                var buildMetadataSeparator = informationalVersion.IndexOf('+');
                return buildMetadataSeparator >= 0
                    ? informationalVersion[..buildMetadataSeparator]
                    : informationalVersion;
            }

            return typeof(CliApplication).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        }
    }
}
