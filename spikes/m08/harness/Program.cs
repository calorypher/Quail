using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var options = HarnessOptions.Parse(args);
NativeInput.EnablePerMonitorDpiAwareness();
Directory.CreateDirectory(options.OutputDirectory);
var metrics = new MetricsWriter(Path.Combine(options.OutputDirectory, "metrics.csv"));
var runner = new PrototypeRunner(options, metrics);
try
{
    await runner.RunAsync();
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    Environment.ExitCode = 1;
}

internal sealed record HarnessOptions(string Framework, string Application, string OutputDirectory, bool Full, bool Smoke, bool Baseline, bool Targeted)
{
    public static HarnessOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var framework = Value("--framework") ?? throw new ArgumentException("--framework is required.");
        var application = Value("--app") ?? throw new ArgumentException("--app is required.");
        var output = Value("--output") ?? Path.Combine("artifacts", "m08");
        return new HarnessOptions(framework, Path.GetFullPath(application), Path.GetFullPath(output), args.Contains("--full"), args.Contains("--smoke"), args.Contains("--baseline"), args.Contains("--targeted"));
    }
}

internal sealed class PrototypeRunner(HarnessOptions options, MetricsWriter metrics)
{
    private const int Warmups = 5;
    private const int StartupRuns = 30;
    private const int HotkeyCycles = 100;
    private const int KeyboardCycles = 50;
    private const int LifecycleCycles = 500;

    public async Task RunAsync()
    {
        if (!File.Exists(options.Application))
        {
            throw new FileNotFoundException("Prototype application was not found.", options.Application);
        }

        if (options.Baseline)
        {
            await RunBaselineAsync();
            Console.WriteLine($"PASS framework={options.Framework} baseline output={options.OutputDirectory}");
            return;
        }

        if (options.Targeted)
        {
            await RunTargetedAsync();
            Console.WriteLine($"PASS framework={options.Framework} targeted output={options.OutputDirectory}");
            return;
        }

        await RunStartupAsync(options.Smoke ? 1 : Warmups, measured: false);
        await RunStartupAsync(options.Smoke ? 1 : StartupRuns, measured: true);
        await RunResidentAsync();
        Console.WriteLine($"PASS framework={options.Framework} output={options.OutputDirectory}");
    }

    private async Task RunStartupAsync(int count, bool measured)
    {
        for (var run = 1; run <= count; run++)
        {
            await using var events = new EventCollector();
            var stopwatch = Stopwatch.StartNew();
            using var child = Start(events.PipeName, "--m08-show-on-start", "--m08-test-exit-after-visible-ready-count", "1");
            try
            {
                var ready = await events.WaitForAsync("visible-ready", TimeSpan.FromSeconds(12));
                stopwatch.Stop();
                var status = ready is null ? "timeout" : "pass";
                metrics.Write(options.Framework, measured ? "cold-startup" : "cold-startup-warmup", run, stopwatch.Elapsed.TotalMilliseconds, status, ready);
                if (ready is null)
                {
                    throw Failure(child, "visible-ready was not received within 12 seconds.");
                }
                await WaitForNormalExitAsync(child, TimeSpan.FromSeconds(5), "startup measurement");
            }
            finally
            {
                Stop(child);
            }
        }
    }

    private async Task RunBaselineAsync()
    {
        await using var events = new EventCollector();
        using var child = Start(events.PipeName, "--m08-show-on-start");
        try
        {
            var startup = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(12));
            RequireWindowAndFocus(startup, child, "startup");

            await BringOwnedChildToForegroundAsync(startup, child);
            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            EnsureResident(child, "initial Escape hide");
            await Task.Delay(100);

            NativeInput.SendHotkey();
            var firstSummon = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(firstSummon, child, "first hotkey summon");

            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            EnsureResident(child, "second Escape hide");
            await Task.Delay(100);

            NativeInput.SendHotkey();
            var secondSummon = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(secondSummon, child, "second hotkey summon");

            await WaitForNormalExitAsync(child, TimeSpan.FromSeconds(5), "baseline");

            metrics.Write(options.Framework, "baseline", 1, 0, "pass", secondSummon);
        }
        catch
        {
            metrics.Write(options.Framework, "baseline", 1, 0, "failure", null);
            throw;
        }
        finally
        {
            Stop(child);
        }
    }

    private async Task RunTargetedAsync()
    {
        await using var events = new EventCollector();
        using var child = Start(events.PipeName);
        try
        {
            await RequireEventAsync(events, child, "startup-hidden", TimeSpan.FromSeconds(5));
            EnsureResident(child, "hidden startup");
            metrics.Write(options.Framework, "startup-hidden", 1, 0, "pass", null);

            NativeInput.SendHotkey();
            var first = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(first, child, "hotkey show");
            NativeInput.SendHotkey();
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            NativeInput.SendHotkey();
            var second = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(second, child, "hotkey re-show");
            metrics.Write(options.Framework, "hotkey-toggle", 1, 0, "pass", second);
            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);

            await RunMonitorCoverageAsync(events, child);

            // Let the dispatcher complete the hide transition before sending the
            // next real global-hotkey input. This is particularly relevant for
            // Avalonia, whose hide operation is queued on its UI dispatcher.
            await Task.Delay(100);

            if (options.Framework == "avalonia")
            {
                NativeInput.SendHotkey();
                _ = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
                for (var index = 0; index < 12; index++) NativeInput.SendVirtualKey(0x28);
                var scroll = await RequireEventAsync(events, child, "selection-scroll-requested", TimeSpan.FromSeconds(3), item => item.Index == 12);
                metrics.Write(options.Framework, "keyboard-scroll-follow", 1, 0, scroll is null ? "failure" : "pass", scroll);
                if (scroll is null) throw Failure(child, "Avalonia did not request scroll-follow for keyboard selection.");
                NativeInput.SendVirtualKey(0x1B);
                await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            }
        }
        finally
        {
            Stop(child);
        }
    }

    private async Task RunResidentAsync()
    {
        var hotkeyCycles = options.Smoke ? 1 : HotkeyCycles;
        var keyboardCycles = options.Smoke ? 1 : KeyboardCycles;
        var lifecycleCycles = options.Smoke ? 2 : LifecycleCycles;
        await using var events = new EventCollector();
        using var child = Start(events.PipeName);
        var process = child.Process;
        try
        {
        await Task.Delay(800);
        await WarmResidentAsync(events, child);
        await RunMonitorCoverageAsync(events, child);
        var before = Snapshot(process);
        metrics.Write(options.Framework, "resource-snapshot", 0, 0, "observed", null, before.WorkingSet, before.PrivateBytes, before.Handles, before.UserObjects, before.GdiObjects);
        var failures = 0;
        for (var cycle = 1; cycle <= hotkeyCycles; cycle++)
        {
            var start = Stopwatch.GetTimestamp();
            NativeInput.SendHotkey();
            var ready = await events.WaitForAsync("visible-ready", TimeSpan.FromSeconds(5));
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var focusOk = ready is not null && ready.Hwnd != 0 && ready.QueryHasKeyboardFocus && (long)NativeInput.GetForegroundWindow() == ready.Hwnd;
            var status = focusOk ? "pass" : "failure";
            if (!focusOk) failures++;
            metrics.Write(options.Framework, "hotkey-visible", cycle, elapsed, status, ready);
            NativeInput.SendVirtualKey(0x1B);
            await events.WaitForAsync("hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);
        }

        events.Discard("visible-ready", "query-changed", "selection-changed", "confirmed", "hidden");
        await Task.Delay(100);

        for (var cycle = 1; cycle <= keyboardCycles; cycle++)
        {
            NativeInput.SendHotkey();
            var ready = await events.WaitForAsync("visible-ready", TimeSpan.FromSeconds(5));
            events.Discard("query-changed", "selection-changed", "confirmed", "hidden");
            NativeInput.SendText("quail");
            var query = await events.WaitForAsync("query-changed", TimeSpan.FromSeconds(3), item => item.Query == "quail");
            events.Discard("selection-changed");
            NativeInput.SendVirtualKey(0x28);
            var down = await events.WaitForAsync("selection-changed", TimeSpan.FromSeconds(3), item => item.Index == 1);
            NativeInput.SendVirtualKey(0x26);
            var up = await events.WaitForAsync("selection-changed", TimeSpan.FromSeconds(3), item => item.Index == 0);
            NativeInput.SendVirtualKey(0x0D);
            var confirmed = await events.WaitForAsync("confirmed", TimeSpan.FromSeconds(3));
            NativeInput.SendVirtualKey(0x1B);
            var hidden = await events.WaitForAsync("hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);
            var passed = ready is not null
                && query is { Query: "quail", ResultCount: > 1 }
                && down is { Index: 1 }
                && up is { Index: 0 }
                && confirmed is not null
                && hidden is not null;
            if (!passed)
            {
                failures++;
                Console.Error.WriteLine($"keyboard-flow failure cycle={cycle} ready={ready is not null} query={query?.Query} down={down?.Index} up={up?.Index} confirmed={confirmed?.Name} hidden={hidden is not null}");
                metrics.Write(options.Framework, "keyboard-flow-ready", cycle, 0, ready is null ? "missing" : "received", ready);
                metrics.Write(options.Framework, "keyboard-flow-query", cycle, 0, query is null ? "missing" : "received", query);
                metrics.Write(options.Framework, "keyboard-flow-down", cycle, 0, down is null ? "missing" : "received", down);
                metrics.Write(options.Framework, "keyboard-flow-up", cycle, 0, up is null ? "missing" : "received", up);
                metrics.Write(options.Framework, "keyboard-flow-confirmed", cycle, 0, confirmed is null ? "missing" : "received", confirmed);
                metrics.Write(options.Framework, "keyboard-flow-hidden", cycle, 0, hidden is null ? "missing" : "received", hidden);
            }
            metrics.Write(options.Framework, "keyboard-flow", cycle, 0, passed ? "pass" : "failure", query);
        }

        for (var cycle = 1; cycle <= lifecycleCycles; cycle++)
        {
            NativeInput.SendHotkey();
            var ready = await events.WaitForAsync("visible-ready", TimeSpan.FromSeconds(5));
            NativeInput.SendVirtualKey(0x1B);
            var hidden = await events.WaitForAsync("hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);
            var passed = !process.HasExited && ready is not null && ready.QueryHasKeyboardFocus && hidden is not null;
            if (!passed) failures++;
            metrics.Write(options.Framework, "summon-escape-lifecycle", cycle, 0, passed ? "pass" : "failure", ready);
            if (cycle is 50 or 100 or 250 || cycle == lifecycleCycles)
            {
                var snapshot = Snapshot(process);
                metrics.Write(options.Framework, "resource-snapshot", cycle, 0, "observed", null, snapshot.WorkingSet, snapshot.PrivateBytes, snapshot.Handles, snapshot.UserObjects, snapshot.GdiObjects);
            }
        }

        await Task.Delay(options.Full ? TimeSpan.FromSeconds(30) : TimeSpan.FromSeconds(2));
        var samples = options.Full ? 120 : 3;
        var cpuStart = process.TotalProcessorTime;
        var wallStart = Stopwatch.StartNew();
        for (var sample = 1; sample <= samples; sample++)
        {
            process.Refresh();
            var sampleSnapshot = Snapshot(process);
            metrics.Write(options.Framework, "hidden-idle", sample, 0, "observed", null, sampleSnapshot.WorkingSet, sampleSnapshot.PrivateBytes, sampleSnapshot.Handles, sampleSnapshot.UserObjects, sampleSnapshot.GdiObjects);
            await Task.Delay(options.Full ? TimeSpan.FromSeconds(1) : TimeSpan.FromMilliseconds(100));
        }
        wallStart.Stop();
        process.Refresh();
        var cpuPercent = (process.TotalProcessorTime - cpuStart).TotalMilliseconds / (wallStart.Elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
        metrics.Write(options.Framework, "idle-cpu", 1, cpuPercent, "pass", null);
        var after = Snapshot(process);
        metrics.Write(options.Framework, "resource-settled", lifecycleCycles, 0, "observed", null, after.WorkingSet, after.PrivateBytes, after.Handles, after.UserObjects, after.GdiObjects);
        if (failures > 0)
        {
            throw new InvalidOperationException($"{options.Framework} resident verification recorded {failures} failures.");
        }
        }
        finally
        {
            Stop(child);
        }
    }

    private StartedProcess Start(string pipeName, params string[] additionalArgs)
    {
        var diagnosticsPath = Path.Combine(options.OutputDirectory, $"{options.Framework}-child-{Guid.NewGuid():N}.log");
        var info = new ProcessStartInfo(options.Application)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(options.Application)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        info.ArgumentList.Add("--m08-pipe");
        info.ArgumentList.Add(pipeName);
        info.ArgumentList.Add("--m08-diagnostics");
        info.ArgumentList.Add(diagnosticsPath);
        if (options.Baseline)
        {
            info.ArgumentList.Add("--m08-test-exit-after-visible-ready-count");
            info.ArgumentList.Add("3");
        }
        foreach (var argument in additionalArgs) info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start prototype.");
        return new StartedProcess(process, diagnosticsPath);
    }

    private static void Stop(StartedProcess child)
    {
        var process = child.Process;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException) { }
    }

    private static async Task<PrototypeEvent> RequireEventAsync(EventCollector events, StartedProcess child, string eventName, TimeSpan timeout, Func<PrototypeEvent, bool>? predicate = null)
    {
        var item = await events.WaitForAsync(eventName, timeout, predicate);
        if (item is not null)
        {
            return item;
        }

        throw Failure(child, $"{eventName} was not received within {timeout.TotalSeconds:F0} seconds.");
    }

    private static async Task RequireForegroundAndFocusAsync(PrototypeEvent item, StartedProcess child, string step)
    {
        RequireWindowAndFocus(item, child, step);

        var deadline = Stopwatch.GetTimestamp() + (2 * Stopwatch.Frequency);
        long foreground;
        do
        {
            foreground = (long)NativeInput.GetForegroundWindow();
            if (foreground == item.Hwnd)
            {
                return;
            }

            await Task.Delay(50);
        }
        while (Stopwatch.GetTimestamp() < deadline && !child.Process.HasExited);

        throw Failure(child, $"{step} did not establish the expected foreground window and textbox focus (hwnd={item.Hwnd}, focus={item.FocusHwnd}, foreground={foreground}).");
    }

    private static void RequireWindowAndFocus(PrototypeEvent item, StartedProcess child, string step)
    {
        if (item.Hwnd == 0 || !item.QueryHasKeyboardFocus)
        {
            throw Failure(child, $"{step} did not report a window handle and framework query focus (hwnd={item.Hwnd}, focus={item.FocusHwnd}, queryFocus={item.QueryHasKeyboardFocus}).");
        }
    }

    private static async Task BringOwnedChildToForegroundAsync(PrototypeEvent item, StartedProcess child)
    {
        NativeInput.SetForegroundWindow((nint)item.Hwnd);
        await RequireForegroundAndFocusAsync(item, child, "startup foreground setup");
    }

    private static void EnsureResident(StartedProcess child, string step)
    {
        if (child.Process.HasExited)
        {
            throw Failure(child, $"The process exited during {step}.");
        }
    }

    private static InvalidOperationException Failure(StartedProcess child, string reason)
    {
        var process = child.Process;
        var state = process.HasExited ? $"exited with code {process.ExitCode}" : "still running";
        return new InvalidOperationException($"{reason} Child PID {process.Id} is {state}. stdout: {child.StandardOutput} stderr: {child.StandardError} diagnostic log: {child.DiagnosticsPath}");
    }

    private static async Task WaitForNormalExitAsync(StartedProcess child, TimeSpan timeout, string step)
    {
        try { await child.Process.WaitForExitAsync().WaitAsync(timeout); }
        catch (TimeoutException) { throw Failure(child, $"The process did not exit through its normal test Exit path after {step}."); }
    }

    private static async Task WarmResidentAsync(EventCollector events, StartedProcess child)
    {
        NativeInput.SendHotkey();
        var ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
        await RequireForegroundAndFocusAsync(ready, child, "resident warm-up");
        await RequireEventAsync(events, child, "shell-icons-ready", TimeSpan.FromSeconds(10));
        NativeInput.SendVirtualKey(0x1B);
        await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
        await Task.Delay(1000);
    }

    private async Task RunMonitorCoverageAsync(EventCollector events, StartedProcess child)
    {
        var monitors = NativeInput.GetMonitors().OrderBy(monitor => monitor.Primary ? 0 : 1).ToArray();
        if (monitors.Length < 2)
        {
            metrics.Write(options.Framework, "monitor-placement", 0, 0, "unavailable", null);
            return;
        }

        var path = new[] { monitors[0], monitors[1], monitors[0] };
        for (var index = 0; index < path.Length; index++)
        {
            var monitor = path[index];
            var point = new NativeInput.Point(monitor.Bounds.Left + monitor.Bounds.Width / 2, monitor.Bounds.Top + monitor.Bounds.Height / 2);
            NativeInput.SetCursorPosition(point.X, point.Y);
            NativeInput.SendHotkey();
            var ready = await RequireEventAsync(events, child, "visible-ready", TimeSpan.FromSeconds(5));
            await RequireForegroundAndFocusAsync(ready, child, $"monitor placement {index + 1}");
            var expectedDpi = NativeInput.GetDpiAt(point);
            var placed = monitor.Bounds.Contains(ready.WindowLeft, ready.WindowTop);
            var expectedWidth = (int)Math.Round(680d * expectedDpi / 96d);
            var expectedHeight = (int)Math.Round(360d * expectedDpi / 96d);
            var correctSize = Math.Abs(ready.WindowWidth - expectedWidth) <= 4 && Math.Abs(ready.WindowHeight - expectedHeight) <= 4;
            var passed = placed && ready.WindowDpi == expectedDpi && correctSize;
            metrics.Write(options.Framework, "monitor-placement", index + 1, ready.WindowDpi, passed ? "pass" : "failure", ready);
            if (!passed)
            {
                throw Failure(child, $"Monitor placement {index + 1} was invalid (left={ready.WindowLeft}, top={ready.WindowTop}, dpi={ready.WindowDpi}, size={ready.WindowWidth}x{ready.WindowHeight}, expected={expectedWidth}x{expectedHeight}).");
            }

            NativeInput.SendVirtualKey(0x1B);
            await RequireEventAsync(events, child, "hidden", TimeSpan.FromSeconds(3));
            await Task.Delay(100);
        }
    }

    private static ResourceSnapshot Snapshot(Process process)
    {
        process.Refresh();
        return new(process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount,
            (int)NativeInput.GetGuiResources(process.Handle, 1), (int)NativeInput.GetGuiResources(process.Handle, 0));
    }
}

internal sealed class StartedProcess(Process process, string diagnosticsPath) : IDisposable
{
    private readonly Task<string> _standardOutput = process.StandardOutput.ReadToEndAsync();
    private readonly Task<string> _standardError = process.StandardError.ReadToEndAsync();
    public Process Process { get; } = process;
    public string DiagnosticsPath { get; } = diagnosticsPath;
    public string StandardOutput => _standardOutput.IsCompletedSuccessfully ? _standardOutput.Result : "<output collection pending>";
    public string StandardError => _standardError.IsCompletedSuccessfully ? _standardError.Result : "<error collection pending>";
    public void Dispose() => Process.Dispose();
}

internal sealed record ResourceSnapshot(long WorkingSet, long PrivateBytes, int Handles, int UserObjects, int GdiObjects);

internal sealed class EventCollector : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<PrototypeEvent> received = [];
    private readonly SemaphoreSlim signal = new(0);
    public string PipeName { get; } = $"quail-m08-{Guid.NewGuid():N}";
    private readonly Task listener;

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
            var remaining = Math.Max(1, (int)((deadline - Stopwatch.GetTimestamp()) * 1000 / Stopwatch.Frequency));
            if (!await signal.WaitAsync(TimeSpan.FromMilliseconds(remaining), cancellation.Token)) break;
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
                    var item = JsonSerializer.Deserialize<PrototypeEvent>(line, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
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

internal sealed record PrototypeEvent(string Event, string? Framework, long Hwnd, long FocusHwnd, bool QueryHasKeyboardFocus, uint WindowDpi, int WindowLeft, int WindowTop, int WindowWidth, int WindowHeight, string? Query, int? ResultCount, int? Index, string? Name);

internal sealed class MetricsWriter(string path)
{
    private readonly object gate = new();
    public void Write(string framework, string scenario, int run, double value, string status, PrototypeEvent? detail, long? workingSet = null, long? privateBytes = null, int? handles = null, int? userObjects = null, int? gdiObjects = null)
    {
        lock (gate)
        {
            var header = !File.Exists(path);
            using var writer = new StreamWriter(path, append: true, Encoding.UTF8);
            if (header) writer.WriteLine("framework,scenario,run,value,status,hwnd,focusHwnd,queryFocus,windowDpi,windowLeft,windowTop,windowWidth,windowHeight,query,resultCount,workingSetBytes,privateBytes,handleCount,userObjects,gdiObjects");
            writer.WriteLine(string.Join(',', [framework, scenario, run.ToString(CultureInfo.InvariantCulture), value.ToString("F3", CultureInfo.InvariantCulture), status,
                detail?.Hwnd.ToString() ?? "", detail?.FocusHwnd.ToString() ?? "", detail?.QueryHasKeyboardFocus.ToString() ?? "", detail?.WindowDpi.ToString() ?? "", detail?.WindowLeft.ToString() ?? "", detail?.WindowTop.ToString() ?? "", detail?.WindowWidth.ToString() ?? "", detail?.WindowHeight.ToString() ?? "", Escape(detail?.Query), detail?.ResultCount?.ToString() ?? "", workingSet?.ToString() ?? "", privateBytes?.ToString() ?? "", handles?.ToString() ?? "", userObjects?.ToString() ?? "", gdiObjects?.ToString() ?? ""]));
        }
    }
    private static string Escape(string? value) => value is null ? "" : $"\"{value.Replace("\"", "\"\"")}\"";
}

internal static class NativeInput
{
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetProcessDpiAwarenessContext(nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(nint process, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] inputs, int size);
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const ushort VkControl = 0x11, VkMenu = 0x12, VkSpace = 0x20;
    public static void EnablePerMonitorDpiAwareness() => _ = SetProcessDpiAwarenessContext((nint)(-4));
    public static void SetCursorPosition(int x, int y)
    {
        if (!SetCursorPos(x, y)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetCursorPos failed.");
    }
    public static uint GetDpiAt(Point point) => GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out var dpi, out _) == 0 ? dpi : 96;
    public static IReadOnlyList<Monitor> GetMonitors()
    {
        var monitors = new List<Monitor>();
        _ = EnumDisplayMonitors(0, 0, (handle, _, _, _) =>
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(handle, ref info)) monitors.Add(new Monitor(info.Monitor, (info.Flags & 1) != 0));
            return true;
        }, 0);
        return monitors;
    }
    public static void SendHotkey() => SendKeys([VkControl, VkMenu, VkSpace]);
    public static void SendVirtualKey(ushort key) => SendKeys([key]);
    public static void SendText(string text) => SendChecked(text.SelectMany(c => new[] { new INPUT { type = InputKeyboard, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 } } }, new INPUT { type = InputKeyboard, U = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = 0x0004 | KeyUp } } } }).ToArray());
    private static void SendKeys(ushort[] keys)
    {
        var inputs = new List<INPUT>();
        foreach (var key in keys) inputs.Add(Key(key, false));
        for (var index = keys.Length - 1; index >= 0; index--) inputs.Add(Key(keys[index], true));
        SendChecked(inputs.ToArray());
    }
    private static void SendChecked(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"SendInput injected {sent} of {inputs.Length} requested keyboard events.");
        }
    }
    private static INPUT Key(ushort key, bool up) => new() { type = InputKeyboard, U = new INPUTUNION { ki = new KEYBDINPUT { wVk = key, dwFlags = up ? KeyUp : 0 } } };
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct Point
    {
        public int X;
        public int Y;
        public Point(int x, int y) { X = x; Y = y; }
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate bool MonitorEnumProc(nint monitor, nint hdc, nint rect, nint data);
    [StructLayout(LayoutKind.Sequential)] public struct Rect
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor; public Rect Work; public uint Flags; }
    public readonly record struct Monitor(Rect Bounds, bool Primary);
}
