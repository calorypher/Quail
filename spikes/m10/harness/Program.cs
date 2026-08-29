// M10 reuses the resident verification methodology of spikes/m08/harness:
// real SendInput, named-pipe lifecycle events, and post-warm-up snapshots.
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var options = HarnessOptions.Parse(args);
NativeInput.EnablePerMonitorDpiAwareness();
Directory.CreateDirectory(options.OutputDirectory);
var summary = new RunSummary(options);
var metrics = new MetricsWriter(Path.Combine(options.OutputDirectory, "metrics.csv"));
var runner = new ProductionRunner(options, metrics, summary);
try
{
    await runner.RunAsync();
    summary.Status = "pass";
    Console.WriteLine($"PASS m10-production-lifecycle output={options.OutputDirectory}");
}
catch (Exception exception)
{
    summary.Status = "failure";
    summary.Failure = exception.ToString();
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}
finally
{
    await File.WriteAllTextAsync(
        Path.Combine(options.OutputDirectory, "summary.json"),
        JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
}

internal sealed record HarnessOptions(string Application, string OutputDirectory, HotkeyDefinition Hotkey, IReadOnlyList<string> IndexPaths, bool Short, bool ExpectClickFailure, bool SkipSettings, bool SkipMouse, bool SkipKeyboard)
{
    public static HarnessOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var application = Value("--app") ?? throw new ArgumentException("--app is required.");
        var output = Value("--output") ?? Path.Combine("artifacts", "m10", "lifecycle-final");
        var hotkey = Value("--hotkey") ?? "Ctrl+Alt+Space";
        var indexes = args
            .Select((value, index) => (value, index))
            .Where(item => item.value == "--index" && item.index + 1 < args.Length)
            .Select(item => Path.GetFullPath(args[item.index + 1]))
            .ToArray();
        return new HarnessOptions(Path.GetFullPath(application), Path.GetFullPath(output), HotkeyDefinition.Parse(hotkey), indexes, args.Contains("--short"), args.Contains("--expect-click-failure"), args.Contains("--skip-settings"), args.Contains("--skip-mouse"), args.Contains("--skip-keyboard"));
    }
}

internal sealed class ProductionRunner(HarnessOptions options, MetricsWriter metrics, RunSummary summary)
{
    private const int LogicalOverlayWidth = 700;
    private const int CompactLogicalHeight = 56;
    private const int ExpandedLogicalHeight = 370;
    private const int SettingsLogicalHeight = 500;
    private const int PhysicalSizeTolerance = 4;
    private int HotkeyCycles => options.Short ? 5 : 100;
    private int KeyboardCycles => options.Short ? 1 : 50;
    private int LifecycleCycles => options.Short ? 20 : 500;
    private static readonly int[] SnapshotCycles = [50, 100, 250, 500];

    public async Task RunAsync()
    {
        if (!File.Exists(options.Application))
        {
            throw new FileNotFoundException("Production self-contained Quail executable was not found.", options.Application);
        }

        var monitors = NativeInput.GetMonitors().OrderBy(monitor => monitor.Primary ? 0 : 1).ToArray();
        if (monitors.Length >= 2)
        {
            var primary = monitors[0].Bounds;
            NativeInput.SetCursorPosition(primary.Left + (primary.Width / 2), primary.Top + (primary.Height / 2));
        }

        await using var events = new EventCollector();
        using var child = Start(events.PipeName);
        var process = child.Process;
        try
        {
            await RequireEventAsync(events, child, "startup-hidden", TimeSpan.FromSeconds(12));
            EnsureResident(child, "hidden startup");
            await RunMixedDpiCoverageAsync(events, child, monitors);
            await WarmResidentAsync(events, child);
            RecordSnapshot(process, 0, "resource-snapshot");

            await RunEmptyStateSizingCycleAsync(events, child);
            if (!options.SkipSettings)
            {
                await RunSettingsLayoutCyclesAsync(events, child, monitors);
            }
            if (!options.SkipMouse)
            {
                await RunMouseClickCycleAsync(events, child);
            }

            for (var cycle = 1; cycle <= HotkeyCycles; cycle++)
            {
                await RunHotkeyCycleAsync(events, child, cycle);
            }

            if (!options.ExpectClickFailure && !options.SkipKeyboard)
            {
                events.Discard("visible-ready", "query-changed", "selection-changed", "confirmed", "hidden");
                await Task.Delay(100);
                for (var cycle = 1; cycle <= KeyboardCycles; cycle++)
                {
                    await RunKeyboardCycleAsync(events, child, cycle);
                }
            }

            events.Discard("visible-ready", "query-changed", "selection-changed", "confirmed", "hidden");
            await Task.Delay(100);
            for (var cycle = 1; cycle <= LifecycleCycles; cycle++)
            {
                await RunLifecycleCycleAsync(events, child, cycle);
                if (SnapshotCycles.Contains(cycle))
                {
                    RecordSnapshot(process, cycle, "resource-snapshot");
                }
            }

            await MeasureSettledIdleAsync(process);
            summary.ResourceAssessment = AssessResources(summary.ResourceSnapshots);
            if (summary.ResourceAssessment == "investigate")
            {
                throw new InvalidOperationException("Resource checkpoints show a material monotonic USER, GDI, or handle growth signal.");
            }
        }
        finally
        {
            Stop(child);
        }
    }

    private async Task WarmResidentAsync(EventCollector events, StartedProcess child)
    {
        NativeInput.SendHotkey(options.Hotkey);
        var ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(ready, child, "resident warm-up");
        await RequireEventAsync(events, child, "shell-icons-ready", TimeSpan.FromSeconds(10));
        NativeInput.SendVirtualKey(0x1B);
        await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
        EnsureResident(child, "resident warm-up hide");
        await Task.Delay(1000);
    }

    private async Task RunEmptyStateSizingCycleAsync(EventCollector events, StartedProcess child)
    {
        NativeInput.SendHotkey(options.Hotkey);
        var compact = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(compact, child, "empty-state compact mode");
        if (compact.Layout != "Compact" || compact.WindowHeight is null)
        {
            throw Failure(child, "empty-state startup did not report compact layout dimensions.");
        }
        RequireCenteredOnCursorMonitor(compact, child, "empty-state compact mode");
        RequireExpectedPhysicalSize(compact, QuickSearchLayoutMode.Compact, child, "empty-state compact mode");

        events.Discard("layout-changed", "query-changed");
        NativeInput.SendText("abc");
        var expanded = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Expanded");
        if (expanded.WindowHeight is null || expanded.WindowHeight <= compact.WindowHeight)
        {
            throw Failure(child, "non-empty query did not expand the Quick Search window.");
        }
        RequireCenteredOnCursorMonitor(expanded, child, "empty-state expanded mode");
        RequireExpectedPhysicalSize(expanded, QuickSearchLayoutMode.Expanded, child, "empty-state expanded mode");

        NativeInput.SendVirtualKey(0x08);
        NativeInput.SendVirtualKey(0x08);
        NativeInput.SendVirtualKey(0x08);
        var compactAfterClear = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Compact");
        if (compactAfterClear.WindowHeight is null || compactAfterClear.WindowHeight >= expanded.WindowHeight)
        {
            throw Failure(child, "clearing the query did not restore compact Quick Search mode.");
        }
        RequireCenteredOnCursorMonitor(compactAfterClear, child, "empty-state restored compact mode");
        RequireExpectedPhysicalSize(compactAfterClear, QuickSearchLayoutMode.Compact, child, "empty-state restored compact mode");

        summary.InitialCompactHeight = compact.WindowHeight;
        summary.InitialCompactWidth = compact.WindowWidth;
        summary.InitialCompactDpi = compact.WindowDpi;
        summary.ExpandedWidth = expanded.WindowWidth;
        summary.ExpandedHeight = expanded.WindowHeight;
        summary.ExpandedDpi = expanded.WindowDpi;
        summary.RestoredCompactWidth = compactAfterClear.WindowWidth;
        summary.RestoredCompactHeight = compactAfterClear.WindowHeight;
        summary.RestoredCompactDpi = compactAfterClear.WindowDpi;

        NativeInput.SendVirtualKey(0x1B);
        await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
        EnsureResident(child, "empty-state sizing cycle hide");
        summary.EmptyStatePasses++;
    }

    private async Task RunMixedDpiCoverageAsync(EventCollector events, StartedProcess child, IReadOnlyList<NativeInput.Monitor> monitors)
    {
        if (monitors.Count < 2)
        {
            summary.MixedDpiStatus = "unavailable";
            metrics.Write("m10", "mixed-dpi-placement", 0, 0, "unavailable", null);
            return;
        }

        summary.MixedDpiStatus = "pass";
        var path = new[] { monitors[1], monitors[0], monitors[1] };
        for (var index = 0; index < path.Length; index++)
        {
            var monitor = path[index];
            NativeInput.SetCursorPosition(
                monitor.Bounds.Left + (monitor.Bounds.Width / 2),
                monitor.Bounds.Top + (monitor.Bounds.Height / 2));
            NativeInput.SendHotkey(options.Hotkey);
            var compact = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(compact, child, $"mixed-DPI compact summon {index + 1}");
            RequireMonitorPlacement(compact, monitor, child, $"mixed-DPI compact summon {index + 1}");
            RequireExpectedPhysicalSize(compact, QuickSearchLayoutMode.Compact, child, $"mixed-DPI compact summon {index + 1}");

            events.Discard("layout-changed", "query-changed");
            NativeInput.SendText("abc");
            var expanded = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Expanded");
            RequireMonitorPlacement(expanded, monitor, child, $"mixed-DPI expanded summon {index + 1}");
            RequireExpectedPhysicalSize(expanded, QuickSearchLayoutMode.Expanded, child, $"mixed-DPI expanded summon {index + 1}");

            NativeInput.SendVirtualKey(0x08);
            NativeInput.SendVirtualKey(0x08);
            NativeInput.SendVirtualKey(0x08);
            var compactAfterClear = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Compact");
            RequireMonitorPlacement(compactAfterClear, monitor, child, $"mixed-DPI restored compact summon {index + 1}");
            RequireExpectedPhysicalSize(compactAfterClear, QuickSearchLayoutMode.Compact, child, $"mixed-DPI restored compact summon {index + 1}");

            summary.MixedDpiObservations.Add(new MixedDpiObservation(
                index + 1,
                compact.WindowDpi ?? 0,
                compact.WindowWidth ?? 0,
                compact.WindowHeight ?? 0,
                expanded.WindowWidth ?? 0,
                expanded.WindowHeight ?? 0,
                compactAfterClear.WindowWidth ?? 0,
                compactAfterClear.WindowHeight ?? 0));
            metrics.Write("m10", "mixed-dpi-placement", index + 1, compact.WindowDpi ?? 0, "pass", compact);

            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);
        }
    }

    private async Task RunSettingsLayoutCyclesAsync(EventCollector events, StartedProcess child, IReadOnlyList<NativeInput.Monitor> monitors)
    {
        var settingsMonitors = monitors
            .GroupBy(monitor => NativeInput.GetDpiForMonitor(monitor.Handle))
            .Select(group => group.First())
            .ToArray();
        if (settingsMonitors.Length == 0)
        {
            await RunSettingsLayoutCycleAsync(events, child);
            return;
        }

        foreach (var monitor in settingsMonitors)
        {
            NativeInput.SetCursorPosition(
                monitor.Bounds.Left + (monitor.Bounds.Width / 2),
                monitor.Bounds.Top + (monitor.Bounds.Height / 2));
            await RunSettingsLayoutCycleAsync(events, child);
        }
    }

    private async Task RunSettingsLayoutCycleAsync(EventCollector events, StartedProcess child)
    {
        NativeInput.SendHotkey(options.Hotkey);
        var compact = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(compact, child, "settings compact cycle");
        RequireExpectedPhysicalSize(compact, QuickSearchLayoutMode.Compact, child, "settings compact cycle");
        await Task.Delay(100);

        events.Discard("layout-changed", "settings-opened", "settings-closed");
        ClickSettingsButton(compact, child);
        var openedFromCompact = await RequireEventAsync(events, child, "settings-opened", TimeSpan.FromSeconds(3));
        RequireSettingsLayout(openedFromCompact, QuickSearchLayoutMode.Expanded, child, "settings opened from compact mode", settingsOpen: true);
        NativeInput.SendVirtualKey(0x0D);
        var closedAfterSave = await RequireEventAsync(events, child, "settings-closed", TimeSpan.FromSeconds(3));
        RequireSettingsLayout(closedAfterSave, QuickSearchLayoutMode.Compact, child, "settings save with empty query");
        RequireQueryFocus(closedAfterSave, child, "settings save with empty query");
        summary.SettingsSavePasses++;

        NativeInput.SendHotkey(options.Hotkey);
        await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
        NativeInput.SendHotkey(options.Hotkey);
        var readyForExpanded = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(readyForExpanded, child, "settings expanded cycle");
        events.Discard("layout-changed", "query-changed", "settings-opened", "settings-closed");
        NativeInput.SendText("abc");
        var expanded = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Expanded");
        RequireExpectedPhysicalSize(expanded, QuickSearchLayoutMode.Expanded, child, "settings expanded cycle");

        events.Discard("layout-changed", "settings-opened", "settings-closed");
        ClickSettingsButton(expanded, child);
        var openedFromExpanded = await RequireEventAsync(events, child, "settings-opened", TimeSpan.FromSeconds(3));
        RequireSettingsLayout(openedFromExpanded, QuickSearchLayoutMode.Expanded, child, "settings opened from expanded mode", settingsOpen: true);
        NativeInput.SendVirtualKey(0x1B);
        var closedAfterCancel = await RequireEventAsync(events, child, "settings-closed", TimeSpan.FromSeconds(3));
        RequireSettingsLayout(closedAfterCancel, QuickSearchLayoutMode.Expanded, child, "settings cancel with query");
        RequireQueryFocus(closedAfterCancel, child, "settings cancel with query");
        summary.SettingsCancelPasses++;
        summary.SettingsDpiObservations.Add(new SettingsDpiObservation(
            openedFromCompact.WindowDpi ?? 0,
            openedFromCompact.WindowWidth ?? 0,
            openedFromCompact.WindowHeight ?? 0,
            closedAfterSave.WindowWidth ?? 0,
            closedAfterSave.WindowHeight ?? 0,
            openedFromExpanded.WindowWidth ?? 0,
            openedFromExpanded.WindowHeight ?? 0,
            closedAfterCancel.WindowWidth ?? 0,
            closedAfterCancel.WindowHeight ?? 0));

        NativeInput.SendVirtualKey(0x1B);
        await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
        EnsureResident(child, "settings layout cycle");
    }

    private async Task RunMouseClickCycleAsync(EventCollector events, StartedProcess child)
    {
        NativeInput.SendHotkey(options.Hotkey);
        var ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(ready, child, "mouse click cycle");
        events.Discard("layout-changed", "query-changed", "selection-changed", "confirmed", "open-failed", "hidden");
        NativeInput.SendText("quail");
        var query = await RequireEventAsync(events, child, "query-changed", TimeSpan.FromSeconds(3), item => item.Query == "quail");
        if (query.ResultCount <= 0)
        {
            throw Failure(child, "mouse click cycle query returned no results.");
        }

        var expanded = await RequireEventAsync(events, child, "layout-changed", TimeSpan.FromSeconds(3), item => item.Layout == "Expanded");
        if (expanded.WindowLeft is null || expanded.WindowTop is null || expanded.WindowWidth is null || expanded.WindowHeight is null)
        {
            throw Failure(child, "mouse click cycle did not report expanded window geometry.");
        }

        await Task.Delay(150);
        NativeInput.SendLeftClick(
            expanded.WindowLeft.Value + (expanded.WindowWidth.Value / 4),
            expanded.WindowTop.Value + (expanded.WindowHeight.Value / 4));

        if (options.ExpectClickFailure)
        {
            var failed = await RequireEventAsync(events, child, "open-failed", TimeSpan.FromSeconds(3));
            if (!failed.QueryHasKeyboardFocus)
            {
                throw Failure(child, "click-open failure did not restore QueryBox focus.");
            }

            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            summary.MouseFailurePasses++;
        }
        else
        {
            await RequireEventAsync(events, child, "confirmed", TimeSpan.FromSeconds(3));
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            summary.MousePasses++;
        }

        EnsureResident(child, "mouse click cycle");
    }

    private async Task RunHotkeyCycleAsync(EventCollector events, StartedProcess child, int cycle)
    {
        PrototypeEvent? ready = null;
        var elapsed = 0d;
        try
        {
            var start = Stopwatch.GetTimestamp();
            NativeInput.SendHotkey(options.Hotkey);
            ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await RequireForegroundAndFocusAsync(ready, child, $"hotkey cycle {cycle}");
            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            EnsureResident(child, $"hotkey cycle {cycle} hide");
            summary.HotkeyPasses++;
            summary.HotkeyLatenciesMilliseconds.Add(elapsed);
            metrics.Write("m10", "hotkey-cycle", cycle, elapsed, "pass", ready);
        }
        catch (Exception exception)
        {
            summary.HotkeyFailures++;
            metrics.Write("m10", "hotkey-cycle", cycle, elapsed, "failure", ready);
            throw Failure(child, $"hotkey cycle {cycle} failed: {exception.Message}");
        }

        await Task.Delay(100);
    }

    private async Task RunKeyboardCycleAsync(EventCollector events, StartedProcess child, int cycle)
    {
        PrototypeEvent? ready = null;
        PrototypeEvent? query = null;
        PrototypeEvent? down = null;
        PrototypeEvent? up = null;
        PrototypeEvent? confirmed = null;
        PrototypeEvent? hidden = null;
        try
        {
            NativeInput.SendHotkey(options.Hotkey);
            ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(ready, child, $"keyboard cycle {cycle}");
            events.Discard("query-changed", "selection-changed", "confirmed", "hidden");
            NativeInput.SendText("quail");
            query = await RequireEventAsync(events, child, "query-changed", TimeSpan.FromSeconds(3), item => item.Query == "quail");
            if (query.ResultCount <= 1)
            {
                throw new InvalidOperationException($"keyboard cycle {cycle} query result count was {query.ResultCount}.");
            }

            events.Discard("selection-changed");
            NativeInput.SendVirtualKey(0x28);
            down = await RequireEventAsync(events, child, "selection-changed", TimeSpan.FromSeconds(3), item => item.Index == 1);
            NativeInput.SendVirtualKey(0x26);
            up = await RequireEventAsync(events, child, "selection-changed", TimeSpan.FromSeconds(3), item => item.Index == 0);
            NativeInput.SendVirtualKey(0x0D);
            confirmed = await RequireEventAsync(events, child, "confirmed", TimeSpan.FromSeconds(3));
            NativeInput.SendVirtualKey(0x1B);
            hidden = await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            EnsureResident(child, $"keyboard cycle {cycle} hide");
            summary.KeyboardPasses++;
            metrics.Write("m10", "keyboard-cycle", cycle, 0, "pass", query);
        }
        catch (Exception exception)
        {
            summary.KeyboardFailures++;
            metrics.Write("m10", "keyboard-cycle", cycle, 0, "failure", query);
            Console.Error.WriteLine($"keyboard failure cycle={cycle} ready={ready is not null} query={query?.Query} down={down?.Index} up={up?.Index} confirmed={confirmed?.Name} hidden={hidden is not null}");
            throw Failure(child, $"keyboard cycle {cycle} failed: {exception.Message}");
        }

        await Task.Delay(100);
    }

    private async Task RunLifecycleCycleAsync(EventCollector events, StartedProcess child, int cycle)
    {
        PrototypeEvent? ready = null;
        try
        {
            NativeInput.SendHotkey(options.Hotkey);
            ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(ready, child, $"lifecycle cycle {cycle}");
            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            EnsureResident(child, $"lifecycle cycle {cycle} hide");
            summary.LifecyclePasses++;
            metrics.Write("m10", "summon-escape-lifecycle", cycle, 0, "pass", ready);
        }
        catch (Exception exception)
        {
            summary.LifecycleFailures++;
            metrics.Write("m10", "summon-escape-lifecycle", cycle, 0, "failure", ready);
            throw Failure(child, $"lifecycle cycle {cycle} failed: {exception.Message}");
        }

        await Task.Delay(100);
    }

    private async Task MeasureSettledIdleAsync(Process process)
    {
        await Task.Delay(TimeSpan.FromSeconds(options.Short ? 2 : 30));
        var cpuStart = process.TotalProcessorTime;
        var wall = Stopwatch.StartNew();
        for (var sample = 1; sample <= (options.Short ? 10 : 120); sample++)
        {
            process.Refresh();
            var snapshot = Snapshot(process);
            metrics.Write("m10", "hidden-idle", sample, 0, "observed", null, snapshot.WorkingSet, snapshot.PrivateBytes, snapshot.Handles, snapshot.UserObjects, snapshot.GdiObjects);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        wall.Stop();
        process.Refresh();
        summary.IdleCpuPercent = (process.TotalProcessorTime - cpuStart).TotalMilliseconds /
            (wall.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        metrics.Write("m10", "idle-cpu", 1, summary.IdleCpuPercent.Value, "pass", null);
        RecordSnapshot(process, LifecycleCycles, "resource-settled");
    }

    private void RecordSnapshot(Process process, int cycle, string scenario)
    {
        var snapshot = Snapshot(process);
        summary.ResourceSnapshots.Add(new ResourceCheckpoint(cycle, scenario, snapshot.WorkingSet, snapshot.PrivateBytes, snapshot.Handles, snapshot.UserObjects, snapshot.GdiObjects));
        metrics.Write("m10", scenario, cycle, 0, "observed", null, snapshot.WorkingSet, snapshot.PrivateBytes, snapshot.Handles, snapshot.UserObjects, snapshot.GdiObjects);
    }

    private static string AssessResources(IReadOnlyList<ResourceCheckpoint> snapshots)
    {
        return IsMaterialMonotonic(snapshots.Select(item => item.UserObjects), 50) ||
            IsMaterialMonotonic(snapshots.Select(item => item.GdiObjects), 50) ||
            IsMaterialMonotonic(snapshots.Select(item => item.HandleCount), 100)
            ? "investigate"
            : "pass";
    }

    private static bool IsMaterialMonotonic(IEnumerable<int> values, int materialDelta)
    {
        var series = values.ToArray();
        return series.Length >= 5 &&
            series.Zip(series.Skip(1), (left, right) => right > left).All(item => item) &&
            series[^1] - series[0] >= materialDelta;
    }

    private StartedProcess Start(string pipeName)
    {
        var diagnosticsPath = Path.Combine(options.OutputDirectory, $"m10-child-{Guid.NewGuid():N}.log");
        var info = new ProcessStartInfo(options.Application)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(options.Application)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("--test-event-pipe");
        info.ArgumentList.Add(pipeName);
        info.ArgumentList.Add("--diagnostics-path");
        info.ArgumentList.Add(diagnosticsPath);
        foreach (var indexPath in options.IndexPaths)
        {
            info.ArgumentList.Add("--index");
            info.ArgumentList.Add(indexPath);
        }
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start production Quail.exe.");
        return new StartedProcess(process, diagnosticsPath);
    }

    private static async Task<PrototypeEvent> RequireEventAsync(EventCollector events, StartedProcess child, string eventName, TimeSpan timeout, Func<PrototypeEvent, bool>? predicate = null)
    {
        var item = await events.WaitForAsync(eventName, timeout, predicate);
        return item ?? throw Failure(child, $"{eventName} was not received within {timeout.TotalSeconds:F0} seconds.");
    }

    private static async Task RequireForegroundAndFocusAsync(PrototypeEvent item, StartedProcess child, string step)
    {
        if (item.Hwnd == 0 || !item.QueryHasKeyboardFocus)
        {
            throw Failure(child, $"{step} did not report a valid HWND and query focus (hwnd={item.Hwnd}, queryFocus={item.QueryHasKeyboardFocus}).");
        }

        var deadline = Stopwatch.GetTimestamp() + (2 * Stopwatch.Frequency);
        do
        {
            if ((long)NativeInput.GetForegroundWindow() == item.Hwnd)
            {
                return;
            }

            await Task.Delay(50);
        }
        while (Stopwatch.GetTimestamp() < deadline && !child.Process.HasExited);

        throw Failure(child, $"{step} did not establish foreground state for HWND {item.Hwnd}.");
    }

    private static void RequireCenteredOnCursorMonitor(PrototypeEvent item, StartedProcess child, string step)
    {
        if (item.WindowLeft is null || item.WindowTop is null || item.WindowWidth is null || item.WindowHeight is null)
        {
            throw Failure(child, $"{step} did not report window geometry.");
        }

        NativeInput.GetCursorPos(out var cursor);
        var monitor = NativeInput.MonitorFromPoint(cursor, NativeInput.MonitorDefaultToNearest);
        var info = new NativeInput.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeInput.MonitorInfo>() };
        if (!NativeInput.GetMonitorInfo(monitor, ref info))
        {
            throw Failure(child, $"{step} could not resolve the cursor monitor.");
        }

        var expectedCenterX = info.Work.Left + ((info.Work.Right - info.Work.Left) / 2);
        var expectedCenterY = info.Work.Top + ((info.Work.Bottom - info.Work.Top) / 2);
        var actualCenterX = item.WindowLeft.Value + (item.WindowWidth.Value / 2);
        var actualCenterY = item.WindowTop.Value + (item.WindowHeight.Value / 2);
        if (Math.Abs(actualCenterX - expectedCenterX) > 2 || Math.Abs(actualCenterY - expectedCenterY) > 2)
        {
            throw Failure(child, $"{step} was not centered on the cursor monitor. expected=({expectedCenterX},{expectedCenterY}) actual=({actualCenterX},{actualCenterY}).");
        }
    }

    private static void RequireMonitorPlacement(PrototypeEvent item, NativeInput.Monitor expectedMonitor, StartedProcess child, string step)
    {
        if (item.WindowLeft is null || item.WindowTop is null || item.WindowDpi is null)
        {
            throw Failure(child, $"{step} did not report window placement and DPI.");
        }

        if (!expectedMonitor.Bounds.Contains(item.WindowLeft.Value, item.WindowTop.Value))
        {
            throw Failure(child, $"{step} was not placed on the target monitor.");
        }

        var expectedDpi = NativeInput.GetDpiForMonitor(expectedMonitor.Handle);
        if (item.WindowDpi.Value != expectedDpi)
        {
            throw Failure(child, $"{step} reported DPI {item.WindowDpi.Value}, expected {expectedDpi}.");
        }

        RequireCenteredOnCursorMonitor(item, child, step);
    }

    private static void RequireExpectedPhysicalSize(PrototypeEvent item, QuickSearchLayoutMode mode, StartedProcess child, string step, int? logicalHeightOverride = null)
    {
        if (item.WindowDpi is null || item.WindowWidth is null || item.WindowHeight is null)
        {
            throw Failure(child, $"{step} did not report physical size and DPI.");
        }

        var logicalHeight = logicalHeightOverride ?? (mode == QuickSearchLayoutMode.Compact ? CompactLogicalHeight : ExpandedLogicalHeight);
        var expectedWidth = ScaleLogicalToPhysical(LogicalOverlayWidth, item.WindowDpi.Value);
        var expectedHeight = ScaleLogicalToPhysical(logicalHeight, item.WindowDpi.Value);
        if (Math.Abs(item.WindowWidth.Value - expectedWidth) > PhysicalSizeTolerance ||
            Math.Abs(item.WindowHeight.Value - expectedHeight) > PhysicalSizeTolerance)
        {
            throw Failure(child, $"{step} physical size was {item.WindowWidth}x{item.WindowHeight} at {item.WindowDpi} DPI; expected approximately {expectedWidth}x{expectedHeight}.");
        }
    }

    private static int ScaleLogicalToPhysical(int logicalPixels, uint dpi) =>
        checked((int)Math.Round(logicalPixels * (double)dpi / 96, MidpointRounding.AwayFromZero));

    private static void ClickSettingsButton(PrototypeEvent item, StartedProcess child)
    {
        NativeInput.SendVirtualKey(0x09);
        NativeInput.SendVirtualKey(0x0D);
    }

    private static void RequireSettingsLayout(PrototypeEvent item, QuickSearchLayoutMode mode, StartedProcess child, string step, bool settingsOpen = false)
    {
        if (item.Layout != mode.ToString())
        {
            throw Failure(child, $"{step} reported layout {item.Layout ?? "<null>"}, expected {mode}.");
        }

        RequireExpectedPhysicalSize(item, mode, child, step, settingsOpen ? SettingsLogicalHeight : null);
        RequireCenteredOnCursorMonitor(item, child, step);
    }

    private static void RequireQueryFocus(PrototypeEvent item, StartedProcess child, string step)
    {
        if (!item.QueryHasKeyboardFocus)
        {
            throw Failure(child, $"{step} did not restore QueryBox focus.");
        }
    }

    private static void EnsureResident(StartedProcess child, string step)
    {
        if (child.Process.HasExited)
        {
            throw Failure(child, $"Process exited during {step}.");
        }
    }

    private static InvalidOperationException Failure(StartedProcess child, string reason)
    {
        var process = child.Process;
        var state = process.HasExited ? $"exited with code {process.ExitCode}" : "still running";
        return new InvalidOperationException($"{reason} Child PID {process.Id} is {state}. stdout: {child.StandardOutput} stderr: {child.StandardError} diagnostic log: {child.DiagnosticsPath}");
    }

    private static void Stop(StartedProcess child)
    {
        try
        {
            if (!child.Process.HasExited)
            {
                child.Process.Kill(entireProcessTree: true);
                child.Process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
    }

    private static ResourceSnapshot Snapshot(Process process)
    {
        process.Refresh();
        return new ResourceSnapshot(
            process.WorkingSet64,
            process.PrivateMemorySize64,
            process.HandleCount,
            (int)NativeInput.GetGuiResources(process.Handle, 1),
            (int)NativeInput.GetGuiResources(process.Handle, 0));
    }
}

internal sealed class StartedProcess(Process process, string diagnosticsPath) : IDisposable
{
    private readonly Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
    private readonly Task<string> standardError = process.StandardError.ReadToEndAsync();
    public Process Process { get; } = process;
    public string DiagnosticsPath { get; } = diagnosticsPath;
    public string StandardOutput => standardOutput.IsCompletedSuccessfully ? standardOutput.Result : "<output collection pending>";
    public string StandardError => standardError.IsCompletedSuccessfully ? standardError.Result : "<error collection pending>";
    public void Dispose() => Process.Dispose();
}

internal sealed class EventCollector : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<PrototypeEvent> received = [];
    private readonly SemaphoreSlim signal = new(0);
    private readonly Task listener;
    public string PipeName { get; } = $"quail-m10-{Guid.NewGuid():N}";

    public EventCollector() => listener = ListenAsync();

    public async Task<PrototypeEvent?> WaitForAsync(string eventName, TimeSpan timeout, Func<PrototypeEvent, bool>? predicate = null)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            lock (received)
            {
                var index = received.FindIndex(item => item.Event == eventName && (predicate?.Invoke(item) ?? true));
                if (index >= 0)
                {
                    var item = received[index];
                    received.RemoveAt(index);
                    return item;
                }
            }

            var milliseconds = Math.Max(1, (int)((deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency));
            if (!await signal.WaitAsync(TimeSpan.FromMilliseconds(milliseconds), cancellation.Token))
            {
                break;
            }
        }

        return null;
    }

    public void Discard(params string[] eventNames)
    {
        lock (received)
        {
            received.RemoveAll(item => eventNames.Contains(item.Event, StringComparer.Ordinal));
        }
    }

    private async Task ListenAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 8, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
                while (await reader.ReadLineAsync(cancellation.Token) is { } line)
                {
                    var item = JsonSerializer.Deserialize<PrototypeEvent>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (item is not null)
                    {
                        lock (received) received.Add(item);
                        signal.Release();
                    }
                }
            }
            catch (OperationCanceledException) { return; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        try { await listener; } catch (OperationCanceledException) { }
        signal.Dispose();
        cancellation.Dispose();
    }
}

internal sealed record PrototypeEvent(
    string Event,
    long Hwnd,
    long FocusHwnd,
    bool QueryHasKeyboardFocus,
    uint? WindowDpi,
    string? Query,
    int? ResultCount,
    int? Index,
    string? Name,
    string? Layout,
    int? WindowLeft,
    int? WindowTop,
    int? WindowWidth,
    int? WindowHeight);

internal enum QuickSearchLayoutMode
{
    Compact,
    Expanded
}

internal sealed record ResourceSnapshot(long WorkingSet, long PrivateBytes, int Handles, int UserObjects, int GdiObjects);

internal sealed record ResourceCheckpoint(int Cycle, string Scenario, long WorkingSetBytes, long PrivateBytes, int HandleCount, int UserObjects, int GdiObjects);

internal sealed record MixedDpiObservation(
    int Step,
    uint Dpi,
    int CompactWidth,
    int CompactHeight,
    int ExpandedWidth,
    int ExpandedHeight,
    int RestoredCompactWidth,
    int RestoredCompactHeight);

internal sealed record SettingsDpiObservation(
    uint Dpi,
    int OpenedFromCompactWidth,
    int OpenedFromCompactHeight,
    int ClosedAfterSaveWidth,
    int ClosedAfterSaveHeight,
    int OpenedFromExpandedWidth,
    int OpenedFromExpandedHeight,
    int ClosedAfterCancelWidth,
    int ClosedAfterCancelHeight);

internal sealed class RunSummary(HarnessOptions options)
{
    public string Application { get; } = options.Application;
    public string Hotkey { get; } = options.Hotkey.DisplayText;
    public string Status { get; set; } = "pending";
    public int HotkeyPasses { get; set; }
    public int HotkeyFailures { get; set; }
    public int EmptyStatePasses { get; set; }
    public int? InitialCompactWidth { get; set; }
    public int? InitialCompactHeight { get; set; }
    public uint? InitialCompactDpi { get; set; }
    public int? ExpandedWidth { get; set; }
    public int? ExpandedHeight { get; set; }
    public uint? ExpandedDpi { get; set; }
    public int? RestoredCompactWidth { get; set; }
    public int? RestoredCompactHeight { get; set; }
    public uint? RestoredCompactDpi { get; set; }
    public string? MixedDpiStatus { get; set; }
    public List<MixedDpiObservation> MixedDpiObservations { get; } = [];
    public List<SettingsDpiObservation> SettingsDpiObservations { get; } = [];
    public int MousePasses { get; set; }
    public int MouseFailurePasses { get; set; }
    public int SettingsSavePasses { get; set; }
    public int SettingsCancelPasses { get; set; }
    public int KeyboardPasses { get; set; }
    public int KeyboardFailures { get; set; }
    public int LifecyclePasses { get; set; }
    public int LifecycleFailures { get; set; }
    public List<double> HotkeyLatenciesMilliseconds { get; } = [];
    public List<ResourceCheckpoint> ResourceSnapshots { get; } = [];
    public double? IdleCpuPercent { get; set; }
    public string? ResourceAssessment { get; set; }
    public string? Failure { get; set; }
}

internal sealed class MetricsWriter(string path)
{
    private readonly object gate = new();

    public void Write(string framework, string scenario, int cycle, double value, string status, PrototypeEvent? detail, long? workingSet = null, long? privateBytes = null, int? handles = null, int? userObjects = null, int? gdiObjects = null)
    {
        lock (gate)
        {
            var header = !File.Exists(path);
            using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (header)
            {
                writer.WriteLine("framework,scenario,cycle,value,status,hwnd,focusHwnd,queryFocus,query,resultCount,index,name,workingSetBytes,privateBytes,handleCount,userObjects,gdiObjects");
            }

            writer.WriteLine(string.Join(',', [
                framework,
                scenario,
                cycle.ToString(CultureInfo.InvariantCulture),
                value.ToString("F3", CultureInfo.InvariantCulture),
                status,
                detail?.Hwnd.ToString() ?? "",
                detail?.FocusHwnd.ToString() ?? "",
                detail?.QueryHasKeyboardFocus.ToString() ?? "",
                Escape(detail?.Query),
                detail?.ResultCount?.ToString() ?? "",
                detail?.Index?.ToString() ?? "",
                Escape(detail?.Name),
                workingSet?.ToString() ?? "",
                privateBytes?.ToString() ?? "",
                handles?.ToString() ?? "",
                userObjects?.ToString() ?? "",
                gdiObjects?.ToString() ?? ""]));
        }
    }

    private static string Escape(string? value) => value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
}

internal readonly record struct HotkeyDefinition(uint Modifiers, ushort VirtualKey, string DisplayText)
{
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;

    public static HotkeyDefinition Parse(string value)
    {
        var parts = value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var modifiers = 0u;
        ushort virtualKey = 0;
        foreach (var part in parts)
        {
            switch (part.ToUpperInvariant())
            {
                case "CTRL": modifiers |= ModControl; break;
                case "ALT": modifiers |= ModAlt; break;
                case "SHIFT": modifiers |= ModShift; break;
                case "WIN": modifiers |= ModWin; break;
                case "SPACE": virtualKey = 0x20; break;
                case var candidate when candidate.Length == 1 && char.IsAsciiLetterOrDigit(candidate[0]): virtualKey = char.ToUpperInvariant(candidate[0]); break;
                default: throw new ArgumentException("--hotkey must use Ctrl, Alt, Shift, or Win plus one letter, digit, or Space.");
            }
        }

        if (modifiers == 0 || virtualKey == 0)
        {
            throw new ArgumentException("--hotkey must include at least one modifier and one key.");
        }

        var display = string.Join('+', new[]
        {
            (modifiers & ModControl) != 0 ? "Ctrl" : null,
            (modifiers & ModAlt) != 0 ? "Alt" : null,
            (modifiers & ModShift) != 0 ? "Shift" : null,
            (modifiers & ModWin) != 0 ? "Win" : null,
            virtualKey == 0x20 ? "Space" : ((char)virtualKey).ToString()
        }.Where(item => item is not null));
        return new HotkeyDefinition(modifiers, virtualKey, display);
    }

    public IReadOnlyList<ushort> ToVirtualKeys()
    {
        var keys = new List<ushort>();
        if ((Modifiers & ModControl) != 0) keys.Add(0x11);
        if ((Modifiers & ModAlt) != 0) keys.Add(0x12);
        if ((Modifiers & ModShift) != 0) keys.Add(0x10);
        if ((Modifiers & ModWin) != 0) keys.Add(0x5B);
        keys.Add(VirtualKey);
        return keys;
    }
}

internal static class NativeInput
{
    private const uint MonitorInfoPrimary = 1;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint value);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);

    [DllImport("shcore.dll", EntryPoint = "GetDpiForMonitor")]
    private static extern int GetDpiForMonitorNative(nint monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    internal static extern uint GetGuiResources(nint process, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, INPUT[] inputs, int size);

    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    internal const uint MonitorDefaultToNearest = 2;

    internal static void EnablePerMonitorDpiAwareness() => _ = SetProcessDpiAwarenessContext((nint)(-4));

    internal static void SendHotkey(HotkeyDefinition hotkey) => SendKeys(hotkey.ToVirtualKeys());

    internal static IReadOnlyList<Monitor> GetMonitors()
    {
        var monitors = new List<Monitor>();
        if (!EnumDisplayMonitors(0, 0, (monitor, _, _, _) =>
            {
                var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                if (GetMonitorInfo(monitor, ref info))
                {
                    monitors.Add(new Monitor(monitor, info.Monitor, (info.Flags & MonitorInfoPrimary) != 0));
                }

                return true;
            }, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        }

        return monitors;
    }

    internal static uint GetDpiForMonitor(nint monitor) =>
        GetDpiForMonitorNative(monitor, 0, out var dpi, out _) == 0 ? dpi : 96;

    internal static void SetCursorPosition(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SetCursorPos failed for ({x}, {y}).");
        }
    }

    internal static void SendVirtualKey(ushort key) => SendKeys([key]);

    internal static void SendText(string text)
    {
        var inputs = text.SelectMany(character => new[]
        {
            new INPUT { Type = InputKeyboard, Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Scan = character, Flags = KeyUnicode } } },
            new INPUT { Type = InputKeyboard, Union = new INPUTUNION { Keyboard = new KEYBDINPUT { Scan = character, Flags = KeyUnicode | KeyUp } } }
        }).ToArray();
        SendChecked(inputs);
    }

    internal static void SendLeftClick(int x, int y)
    {
        SetCursorPosition(x, y);

        SendChecked([
            new INPUT { Type = InputMouse, Union = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = MouseLeftDown } } },
            new INPUT { Type = InputMouse, Union = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = MouseLeftUp } } }
        ]);
    }

    private static void SendKeys(IReadOnlyList<ushort> keys)
    {
        var inputs = new List<INPUT>();
        foreach (var key in keys) inputs.Add(Key(key, false));
        for (var index = keys.Count - 1; index >= 0; index--) inputs.Add(Key(keys[index], true));
        SendChecked(inputs.ToArray());
    }

    private static void SendChecked(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"SendInput injected {sent} of {inputs.Length} requested keyboard events.");
        }
    }

    private static INPUT Key(ushort key, bool up) => new()
    {
        Type = InputKeyboard,
        Union = new INPUTUNION { Keyboard = new KEYBDINPUT { VirtualKey = key, Flags = up ? KeyUp : 0 } }
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint Type; public INPUTUNION Union; }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
        [FieldOffset(0)] public MOUSEINPUT Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);

    internal readonly record struct Monitor(nint Handle, Rect Bounds, bool Primary);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }
}
