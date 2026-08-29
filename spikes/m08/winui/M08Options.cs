namespace Quail.M08.WinUi;

public sealed record M08Options(string? PipeName, string Theme, bool ShowOnStart, string? DiagnosticsPath, int? TestExitAfterVisibleReadyCount)
{
    public static M08Options Parse(IEnumerable<string> arguments)
    {
        string? pipeName = null;
        var theme = "system";
        var showOnStart = false;
        string? diagnosticsPath = null;
        int? testExitAfterVisibleReadyCount = null;
        var values = arguments.ToArray();

        for (var index = 0; index < values.Length; index++)
        {
            switch (values[index])
            {
                case "--m08-pipe" when index + 1 < values.Length:
                    pipeName = values[++index];
                    break;
                case "--m08-theme" when index + 1 < values.Length:
                    theme = values[++index].ToLowerInvariant();
                    break;
                case "--m08-show-on-start":
                    showOnStart = true;
                    break;
                case "--m08-diagnostics" when index + 1 < values.Length:
                    diagnosticsPath = values[++index];
                    break;
                case "--m08-test-exit-after-visible-ready-count" when index + 1 < values.Length:
                    testExitAfterVisibleReadyCount = int.Parse(values[++index]);
                    break;
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
