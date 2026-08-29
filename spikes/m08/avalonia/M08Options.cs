namespace Quail.M08.Avalonia;

internal sealed record M08Options(string? PipeName, string Theme, bool ShowOnStart, string? DiagnosticsPath, int? TestExitAfterVisibleReadyCount)
{
    public static M08Options Parse(string[] args)
    {
        string? pipeName = null;
        var theme = "system";
        var showOnStart = false;
        string? diagnosticsPath = null;
        int? testExitAfterVisibleReadyCount = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--m08-pipe" when index + 1 < args.Length:
                    pipeName = args[++index];
                    break;
                case "--m08-theme" when index + 1 < args.Length:
                    theme = args[++index].ToLowerInvariant();
                    break;
                case "--m08-show-on-start":
                    showOnStart = true;
                    break;
                case "--m08-diagnostics" when index + 1 < args.Length:
                    diagnosticsPath = args[++index];
                    break;
                case "--m08-test-exit-after-visible-ready-count" when index + 1 < args.Length:
                    testExitAfterVisibleReadyCount = int.Parse(args[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unsupported M08 argument: {args[index]}");
            }
        }

        if (theme is not ("system" or "light" or "dark"))
        {
            throw new ArgumentException("--m08-theme must be system, light, or dark.");
        }

        if (testExitAfterVisibleReadyCount is <= 0)
        {
            throw new ArgumentException("--m08-test-exit-after-visible-ready-count must be positive.");
        }

        return new M08Options(pipeName, theme, showOnStart, diagnosticsPath, testExitAfterVisibleReadyCount);
    }
}
