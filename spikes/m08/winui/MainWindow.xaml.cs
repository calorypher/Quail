using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quail.M08.WinUi.Models;
using WinRT.Interop;
using Windows.Graphics;
using Windows.System;

namespace Quail.M08.WinUi;

public sealed partial class MainWindow : Window
{
    private const int OverlayWidth = 680;
    private const int OverlayHeight = 360;
    private const int HotKeyId = 8;
    private readonly M08Options _options;
    private readonly M08PipeClient _pipe;
    private readonly ObservableCollection<MockResult> _visibleResults = [];
    private readonly ShellIconLoader _shellIcons = new();
    private IReadOnlyList<MockResult> _allResults = [];
    private nint _windowHandle;
    private Win32WindowHook? _windowHook;
    private TrayIconController? _trayIcon;
    private bool _overlayVisible;
    private bool _exiting;
    private bool _shellIconsRequested;
    private int _visibleReadyCount;

    public MainWindow(M08Options options)
    {
        _options = options;
        _pipe = new M08PipeClient(options.PipeName);
        InitializeComponent();
        ResultsList.ItemsSource = _visibleResults;
        ApplyTheme(options.Theme);
        Closed += OnClosed;
        Activated += OnActivated;
    }

    public async Task InitializeAsync()
    {
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: connecting named pipe.");
        await _pipe.ConnectAsync();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: named pipe connected.");
        _allResults = await LoadResultsAsync();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: results loaded.");
        ApplyFilter();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: results filter applied.");

        _windowHandle = WindowNative.GetWindowHandle(this);
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, $"InitializeAsync: HWND acquired ({_windowHandle}).");
        ConfigureAppWindow();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: AppWindow configured.");
        _windowHook = new Win32WindowHook(_windowHandle, OnWindowMessage);
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: window hook installed.");
        RegisterHotKey();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: global hotkey registered.");
        _trayIcon = new TrayIconController(_windowHandle);
        _trayIcon.Add();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "InitializeAsync: tray icon added.");

        if (_options.ShowOnStart)
        {
            ShowOverlay();
        }
        else
        {
            _pipe.Emit(new { @event = "startup-hidden" });
        }
    }

    private async Task<IReadOnlyList<MockResult>> LoadResultsAsync()
    {
        var datasetPath = Path.Combine(AppContext.BaseDirectory, "mock-results.json");
        await using var stream = File.OpenRead(datasetPath);
        var rows = await JsonSerializer.DeserializeAsync<List<MockResultData>>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        })
            ?? throw new InvalidOperationException("The shared M08 mock dataset is empty or invalid.");

        var results = rows.Select(row => new MockResult
        {
            Kind = row.Kind,
            Name = row.Name,
            Path = row.Path,
            Extension = row.Extension,
            SizeBytes = row.SizeBytes,
            ModifiedUtc = row.ModifiedUtc
        }).ToList();

        return results;
    }

    private void ConfigureAppWindow()
    {
        var appWindow = AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        appWindow.Resize(new SizeInt32(OverlayWidth, OverlayHeight));
    }

    private void RegisterHotKey()
    {
        if (!NativeMethods.RegisterHotKey(_windowHandle, HotKeyId, NativeMethods.ModControl | NativeMethods.ModAlt, NativeMethods.VkSpace))
        {
            throw new InvalidOperationException($"RegisterHotKey(Ctrl+Alt+Space) failed with Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private bool OnWindowMessage(uint message, nint wParam, nint lParam)
    {
        if (message == NativeMethods.WmHotKey && (int)wParam == HotKeyId)
        {
            if (_overlayVisible)
            {
                HideOverlay();
            }
            else
            {
                ShowOverlay();
            }

            return true;
        }

        return _trayIcon?.HandleMessage(message, wParam, lParam, ShowOverlay, ExitApplication) ?? false;
    }

    private void ShowOverlay()
    {
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "ShowOverlay entered.");
        if (_exiting)
        {
            return;
        }

        PositionOnCursorMonitor();
        QueryBox.Text = string.Empty;
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "ShowOverlay: positioned on cursor monitor.");
        Activate();
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "ShowOverlay: activated.");
        NativeMethods.SetForegroundWindow(_windowHandle);
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "ShowOverlay: foreground requested.");
        _overlayVisible = true;
        QueryBox.Focus(FocusState.Programmatic);
        M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "ShowOverlay: query box focused.");
        QueueVisibleReadyAfterRender();
    }

    private void RequestShellIcons()
    {
        if (_shellIconsRequested)
        {
            return;
        }

        _shellIconsRequested = true;
        _ = LoadShellIconsAsync();
    }

    private async Task LoadShellIconsAsync()
    {
        try
        {
            foreach (var result in _allResults)
            {
                result.IconSource = await _shellIcons.LoadAsync(result.Kind, result.Extension);
            }

            _pipe.Emit(new { @event = "shell-icons-ready" });
        }
        catch (Exception exception)
        {
            M08Diagnostics.Write(_options.DiagnosticsPath, exception);
        }
    }

    private void HideOverlay()
    {
        if (!_overlayVisible || _exiting)
        {
            return;
        }

        _overlayVisible = false;
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
        _pipe.Emit(new { @event = "hidden" });
    }

    private void PositionOnCursorMonitor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var monitorInfo = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var work = monitorInfo.Work;
        var x = work.Left + ((work.Right - work.Left - OverlayWidth) / 2);
        var y = work.Top + ((work.Bottom - work.Top - OverlayHeight) / 2);
        AppWindow.Move(new PointInt32(x, y));
    }

    private void QueueVisibleReadyAfterRender()
    {
        void OnRendering(object? _, object __)
        {
            CompositionTarget.Rendering -= OnRendering;
            QueryBox.Focus(FocusState.Programmatic);
            NativeMethods.DwmFlush();
            NativeMethods.GetWindowRect(_windowHandle, out var windowRect);
            var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = windowRect.Left, Y = windowRect.Top }, NativeMethods.MonitorDefaultToNearest);
            CenterOnMonitor(monitor, windowRect);
            NativeMethods.DwmFlush();
            NativeMethods.GetWindowRect(_windowHandle, out windowRect);
            monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = windowRect.Left, Y = windowRect.Top }, NativeMethods.MonitorDefaultToNearest);
            uint dpiY;
            var dpi = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out dpiY) == 0 ? dpiX : 96;
            _pipe.Emit(new
            {
                @event = "visible-ready",
                framework = "winui",
                hwnd = (long)_windowHandle,
                focusHwnd = (long)NativeMethods.GetFocus(),
                queryHasKeyboardFocus = ReferenceEquals(FocusManager.GetFocusedElement(QueryBox.XamlRoot), QueryBox),
                windowDpi = dpi,
                windowLeft = windowRect.Left,
                windowTop = windowRect.Top,
                windowWidth = windowRect.Right - windowRect.Left,
                windowHeight = windowRect.Bottom - windowRect.Top
            });
            _visibleReadyCount++;
            if (_options.TestExitAfterVisibleReadyCount == _visibleReadyCount)
            {
                ExitApplication();
                return;
            }
            DispatcherQueue.TryEnqueue(RequestShellIcons);
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void CenterOnMonitor(nint monitor, NativeMethods.Rect windowRect)
    {
        var monitorInfo = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var work = monitorInfo.Work;
        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        AppWindow.Move(new PointInt32(
            work.Left + ((work.Right - work.Left - width) / 2),
            work.Top + ((work.Bottom - work.Top - height) / 2)));
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs args)
    {
        ApplyFilter();
        _pipe.Emit(new { @event = "query-changed", query = QueryBox.Text, resultCount = _visibleResults.Count });
    }

    private void ApplyFilter()
    {
        var query = QueryBox?.Text?.Trim() ?? string.Empty;
        var matching = string.IsNullOrWhiteSpace(query)
            ? _allResults
            : _allResults.Where(result => result.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || result.Path.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();

        _visibleResults.Clear();
        foreach (var result in matching)
        {
            _visibleResults.Add(result);
        }

        ResultsList.SelectedIndex = _visibleResults.Count > 0 ? 0 : -1;
        StatusText.Text = _visibleResults.Count == 0 ? "No matching mock results" : $"{_visibleResults.Count} mock results";
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (ResultsList.SelectedItem is MockResult result)
        {
            _pipe.Emit(new { @event = "selection-changed", index = ResultsList.SelectedIndex, name = result.Name });
        }
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Down:
                MoveSelection(1);
                args.Handled = true;
                break;
            case VirtualKey.Up:
                MoveSelection(-1);
                args.Handled = true;
                break;
            case VirtualKey.Enter when ResultsList.SelectedItem is MockResult result:
                _pipe.Emit(new { @event = "confirmed", name = result.Name });
                args.Handled = true;
                break;
            case VirtualKey.Escape:
                HideOverlay();
                args.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_visibleResults.Count == 0)
        {
            return;
        }

        var selectedIndex = ResultsList.SelectedIndex < 0 ? 0 : ResultsList.SelectedIndex;
        selectedIndex = Math.Clamp(selectedIndex + delta, 0, _visibleResults.Count - 1);
        ResultsList.SelectedIndex = selectedIndex;
        ResultsList.ScrollIntoView(ResultsList.SelectedItem);
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated || !_overlayVisible || _exiting)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_overlayVisible && NativeMethods.GetForegroundWindow() != _windowHandle)
            {
                HideOverlay();
            }
        });
    }

    private void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_windowHandle != 0)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotKeyId);
        }

        _windowHook?.Dispose();
        _windowHook = null;
        _pipe.Dispose();
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_exiting)
        {
            ExitApplication();
        }
    }

    private void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private sealed record MockResultData(string Kind, string Name, string Path, string Extension, long? SizeBytes, DateTime ModifiedUtc);
}
