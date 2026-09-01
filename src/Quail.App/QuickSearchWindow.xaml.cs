using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using WinRT.Interop;
using Windows.Graphics;
using Windows.System;
using Quail.Core;

namespace Quail.App;

public sealed partial class QuickSearchWindow : Window, IDisposable
{
    private const int HotkeyId = 10;
    private const int ExpandedContentTransitionMilliseconds = 140;
    private readonly AppLaunchOptions _options;
    private readonly SettingsStore _settingsStore;
    private readonly SearchRuntime _searchRuntime;
    private readonly Action _exitApplication;
    private readonly Action _showIndexManager;
    private readonly TestEventPipeClient _pipe;
    private readonly SearchPerformanceTrace _searchTrace;
    private readonly SearchPerformanceScenario? _searchPerformanceScenario;
    private readonly ObservableCollection<ResultItem> _visibleResults = [];
    private readonly SearchApplicationService _searchService;
    private readonly LatestSearchCoordinator _interactiveSearchCoordinator;
    private readonly LatestSearchCoordinator _shortQuerySearchCoordinator;
    private readonly ShortQueryDeferrer _shortQueryDeferrer;
    private readonly ShellIconCache _shellIcons = new();
    private ShellSettings _settings;
    private nint _windowHandle;
    private nint _applicationSmallIcon;
    private nint _applicationLargeIcon;
    private Win32WindowHook? _windowHook;
    private TrayIconController? _trayIcon;
    private readonly HotkeyCaptureSession _hotkeyCaptureSession = new();
    private HotkeyDefinition _registeredHotkey;
    private HotkeyDefinition _captureOriginalHotkey;
    private bool _isHotkeyRegistered;
    private bool _overlayVisible;
    private bool _exiting;
    private int _visibleReadyCount;
    private long _queryGeneration;
    private int _selectedResultIndex = -1;
    private QuickSearchOverlayMode _overlayMode = QuickSearchOverlayMode.Expanded;
    private bool _shellIconFailureLogged;
    private bool _settingsDialogActive;
    private long? _searchPerformanceAwaitedUiGeneration;
    private TaskCompletionSource? _searchPerformanceRenderCompletion;

    internal string CurrentTheme => _settings.Theme;

    internal QuickSearchWindow(AppLaunchOptions options, SettingsStore settingsStore, SearchRuntime searchRuntime, ShellSettings settings, Action exitApplication, Action showIndexManager)
    {
        _options = options;
        _settingsStore = settingsStore;
        _searchRuntime = searchRuntime;
        _settings = settings;
        _exitApplication = exitApplication;
        _showIndexManager = showIndexManager;
        _pipe = new TestEventPipeClient(options.TestEventPipeName);
        _searchTrace = new SearchPerformanceTrace(options.SearchPerformanceTracePath, options.SearchPerformanceSessionKind);
        _searchPerformanceScenario = options.SearchPerformanceScenarioPath is null
            ? null
            : SearchPerformanceScenario.Load(options.SearchPerformanceScenarioPath);
        _searchService = searchRuntime.Search;
        _interactiveSearchCoordinator = new LatestSearchCoordinator(
            query => _searchService.Search(new SearchRequest(query)),
            _searchTrace.IsEnabled ? _searchTrace.RecordCoordinator : null,
            SearchExecutionLane.Interactive);
        _interactiveSearchCoordinator.Completed += OnSearchCompleted;
        _shortQuerySearchCoordinator = new LatestSearchCoordinator(
            query => _searchService.Search(new SearchRequest(query)),
            _searchTrace.IsEnabled ? _searchTrace.RecordCoordinator : null,
            SearchExecutionLane.ShortQuery);
        _shortQuerySearchCoordinator.Completed += OnSearchCompleted;
        _searchRuntime.SourcesChanged += OnSourcesChanged;
        _shortQueryDeferrer = new ShortQueryDeferrer(QuickSearchInputPolicy.ShortQueryDefer, OnShortQueryReady);
        InitializeComponent();
        FeatherImage.Source = new SvgImageSource(new Uri("ms-appx:///Assets/quail-feather-A-gradient.svg"));
        ResultsList.ItemsSource = _visibleResults;
        ApplyTheme(settings.Theme);
        ClearResults();
        Closed += OnClosed;
        Activated += OnActivated;
    }

    public async Task InitializeAsync()
    {
        await _pipe.ConnectAsync();
        if (_searchTrace.IsEnabled)
        {
            _searchRuntime.RecordSessionStart(_searchTrace);
        }
        _windowHandle = WindowNative.GetWindowHandle(this);
        ConfigureAppWindow();
        ConfigureNativeWindowPresentation();
        _applicationSmallIcon = BrandingAssets.CreateApplicationSmallIcon();
        _applicationLargeIcon = BrandingAssets.CreateApplicationLargeIcon();
        NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, NativeMethods.IconSmall, _applicationSmallIcon);
        NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, NativeMethods.IconBig, _applicationLargeIcon);
        _windowHook = new Win32WindowHook(_windowHandle, OnWindowMessage);
        if (!TryRegisterHotkey(_settings.Hotkey, out var error))
        {
            throw new InvalidOperationException(error);
        }

        _trayIcon = new TrayIconController(_windowHandle);
        if (!_trayIcon.TryAdd())
        {
            AppLog.Write("Tray integration is unavailable in this desktop session; the resident shell remains active.");
        }
        AppLog.Write($"Hotkey registered: {_registeredHotkey.DisplayText}.");

        if (_options.ShowOnStart)
        {
            ShowOverlay();
        }
        else
        {
            _pipe.Emit(new { @event = "startup-hidden" });
            AppLog.Write("Startup hidden.");
        }
    }

    public void ShowOverlay()
    {
        if (_exiting)
        {
            return;
        }

        if (QuickSearchLifecycle.GetSummonBehavior(_overlayVisible, _settingsDialogActive) == QuickSearchSummonBehavior.ActivateExistingSettings)
        {
            Activate();
            NativeMethods.SetForegroundWindow(_windowHandle);
            AppLog.Write("Summon activated existing Settings dialog.");
            return;
        }

        var targetMonitor = GetCursorMonitor();
        MoveToMonitor(targetMonitor);
        NativeMethods.DwmFlush();
        ApplyOverlayMode(QuickSearchOverlayMode.Compact, recenter: false, forceResize: true, animateExpandedContent: false);
        NativeMethods.DwmFlush();
        CenterActualWindowOnMonitor(targetMonitor, GetWindowRect());
        QueryBox.Text = string.Empty;
        Activate();
        NativeMethods.SetForegroundWindow(_windowHandle);
        _overlayVisible = true;
        QueryBox.Focus(FocusState.Programmatic);
        AppLog.Write("Summon requested.");
        QueueVisibleReadyAfterRender();
    }

    public void Dispose()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _trayIcon?.Dispose();
        _trayIcon = null;
        if (_windowHandle != 0 && _isHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _isHotkeyRegistered = false;
        }
        _windowHook?.Dispose();
        _windowHook = null;
        if (_applicationSmallIcon != 0)
        {
            NativeMethods.DestroyIcon(_applicationSmallIcon);
            _applicationSmallIcon = 0;
        }
        if (_applicationLargeIcon != 0)
        {
            NativeMethods.DestroyIcon(_applicationLargeIcon);
            _applicationLargeIcon = 0;
        }
        _pipe.Dispose();
        _shortQueryDeferrer.Dispose();
        _searchRuntime.SourcesChanged -= OnSourcesChanged;
        _interactiveSearchCoordinator.Dispose();
        _shortQuerySearchCoordinator.Dispose();
        _shellIcons.Dispose();
        _searchTrace.Dispose();
        AppLog.Write("Tray Exit.");
        Close();
    }

    private void ConfigureAppWindow()
    {
        AppWindow.Title = "Quail Quick Search";
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        ApplyOverlayMode(QuickSearchOverlayMode.Compact, recenter: false, forceResize: true, animateExpandedContent: false);
    }

    private void ConfigureNativeWindowPresentation()
    {
        var previousStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlStyle);
        var borderlessStyle = previousStyle & ~NativeMethods.WsDlgFrame;
        NativeMethods.SetLastError(0);
        var styleSetResult = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlStyle, borderlessStyle);
        var styleSetError = Marshal.GetLastWin32Error();
        if (styleSetResult == 0 && styleSetError != 0)
        {
            AppLog.Write($"SetWindowLongPtr(GWL_STYLE) white-border workaround failed with Win32 error {styleSetError}.");
        }

        var previousExtendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle);
        var transientExtendedStyle = (previousExtendedStyle | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExAppWindow;
        NativeMethods.SetLastError(0);
        var setResult = NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GwlExStyle, transientExtendedStyle);
        var setError = Marshal.GetLastWin32Error();
        if (setResult == 0 && setError != 0)
        {
            AppLog.Write($"SetWindowLongPtr(GWL_EXSTYLE) failed with Win32 error {setError}.");
        }
        else
        {
            var frameChanged = NativeMethods.SetWindowPos(
                _windowHandle,
                0,
                0,
                0,
                0,
                0,
                NativeMethods.SwpNoSize |
                NativeMethods.SwpNoMove |
                NativeMethods.SwpNoZOrder |
                NativeMethods.SwpNoActivate |
                NativeMethods.SwpFrameChanged);
            AppLog.Write($"Window styles applied stylePrevious=0x{(ulong)previousStyle:X} styleCurrent=0x{(ulong)borderlessStyle:X} extendedPrevious=0x{(ulong)previousExtendedStyle:X} extendedCurrent=0x{(ulong)transientExtendedStyle:X} frameChanged={frameChanged} error={(frameChanged ? 0 : Marshal.GetLastWin32Error())}.");
        }

        var borderColor = NativeMethods.DwmColorNone;
        var borderResult = NativeMethods.DwmSetWindowAttribute(
            _windowHandle,
            NativeMethods.DwmwaBorderColor,
            ref borderColor,
            sizeof(uint));
        if (borderResult != 0)
        {
            AppLog.Write($"DwmSetWindowAttribute(DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE) failed with HRESULT 0x{borderResult:X8}.");
        }
        else
        {
            AppLog.Write("DwmSetWindowAttribute(DWMWA_BORDER_COLOR, DWMWA_COLOR_NONE) applied.");
        }
    }

    private bool OnWindowMessage(uint message, nint wParam, nint lParam)
    {
        if (message == NativeMethods.WmHotKey && (int)wParam == HotkeyId)
        {
            if (!QuickSearchLifecycle.ShouldToggleOverlayFromHotkey(_settingsDialogActive))
            {
                ShowOverlay();
            }
            else if (_overlayVisible)
            {
                HideOverlay("hotkey-toggle");
            }
            else
            {
                ShowOverlay();
            }
            return true;
        }
        return _trayIcon?.HandleMessage(message, wParam, lParam, ShowOverlay, ShowSettings, _exitApplication) ?? false;
    }

    private bool TryRegisterHotkey(string value, out string error)
    {
        error = string.Empty;
        if (!HotkeyDefinition.TryParse(value, out var requested))
        {
            error = "Hotkey must use Ctrl, Alt, Shift, or Win plus one letter, digit, or Space.";
            return false;
        }
        if (_isHotkeyRegistered)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId);
            _isHotkeyRegistered = false;
        }
        if (!NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, requested.Modifiers, requested.VirtualKey))
        {
            var restored = false;
            if (_registeredHotkey.VirtualKey != 0)
            {
                restored = NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, _registeredHotkey.Modifiers, _registeredHotkey.VirtualKey);
            }
            _isHotkeyRegistered = restored;
            error = restored
                ? "That hotkey is unavailable. The previous Quail hotkey remains active."
                : "That hotkey is unavailable, and Quail could not restore the previous hotkey.";
            AppLog.Write($"Hotkey registration failed: {requested.DisplayText}; previous hotkey restored={restored}.");
            return false;
        }
        _registeredHotkey = requested;
        _isHotkeyRegistered = true;
        HotkeyText.Text = requested.DisplayText;
        return true;
    }

    private async void ShowSettings()
    {
        if (_settingsDialogActive)
        {
            return;
        }

        _settingsDialogActive = true;
        if (!_overlayVisible)
        {
            ShowOverlay();
        }

        var manageIndexesRequested = false;
        try
        {
            await ApplySettingsHostLayoutAsync();
            var settingsSurface = new SettingsSurface(_settings, TryApplySettingsAsync, BeginHotkeyCapture, RestoreHotkeyAfterCapture);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            settingsSurface.Closed += () => closed.TrySetResult();
            SettingsHost.Children.Add(settingsSurface);
            SettingsHost.Visibility = Visibility.Visible;
            EmitSettingsLayoutEvent("settings-opened");
            await closed.Task;
            manageIndexesRequested = settingsSurface.ManageIndexesRequested;
        }
        finally
        {
            SettingsHost.Children.Clear();
            SettingsHost.Visibility = Visibility.Collapsed;
            _settingsDialogActive = false;
            await ApplyOverlayModeAsync(QuickSearchOverlayLayout.ForQuery(QueryBox.Text.Trim()), forceResize: true);
            QueryBox.Focus(FocusState.Programmatic);
            EmitSettingsLayoutEvent("settings-closed");
        }
        if (manageIndexesRequested)
        {
            HideOverlay("manage-indexes");
            _showIndexManager();
            _pipe.Emit(new { @event = "settings-manage-indexes" });
        }
    }

    private async Task<string?> TryApplySettingsAsync(ShellSettings proposed)
    {
        proposed = proposed.Normalize();
        if (!TryRegisterHotkey(proposed.Hotkey, out var error))
        {
            ResumeHotkeyCaptureAfterRegistrationFailure();
            return error;
        }
        _hotkeyCaptureSession.CompleteSave();
        _settings = proposed;
        ApplyTheme(proposed.Theme);
        await _settingsStore.SaveAsync(proposed);
        return null;
    }

    private void BeginHotkeyCapture()
    {
        if (!_hotkeyCaptureSession.Begin())
        {
            return;
        }

        _captureOriginalHotkey = _registeredHotkey;
        SuspendRegisteredHotkeyForCapture();
    }

    private void ResumeHotkeyCaptureAfterRegistrationFailure()
    {
        if (_hotkeyCaptureSession.IsActive)
        {
            SuspendRegisteredHotkeyForCapture();
        }
    }

    private void SuspendRegisteredHotkeyForCapture()
    {
        if (!_isHotkeyRegistered || _registeredHotkey.VirtualKey == 0)
        {
            return;
        }

        if (NativeMethods.UnregisterHotKey(_windowHandle, HotkeyId))
        {
            _isHotkeyRegistered = false;
            AppLog.Write($"Hotkey capture suspended {_registeredHotkey.DisplayText}.");
        }
        else
        {
            AppLog.Write($"Hotkey capture could not suspend {_registeredHotkey.DisplayText}; Win32 error {Marshal.GetLastWin32Error()}.");
        }
    }

    private bool RestoreHotkeyAfterCapture()
    {
        if (!_hotkeyCaptureSession.IsActive || _isHotkeyRegistered || _captureOriginalHotkey.VirtualKey == 0)
        {
            _hotkeyCaptureSession.CompleteCancel(true);
            return true;
        }

        if (NativeMethods.RegisterHotKey(_windowHandle, HotkeyId, _captureOriginalHotkey.Modifiers, _captureOriginalHotkey.VirtualKey))
        {
            _registeredHotkey = _captureOriginalHotkey;
            _isHotkeyRegistered = true;
            HotkeyText.Text = _registeredHotkey.DisplayText;
            _hotkeyCaptureSession.CompleteCancel(true);
            AppLog.Write($"Hotkey capture restored {_registeredHotkey.DisplayText}.");
            return true;
        }

        AppLog.Write($"Hotkey capture could not restore {_captureOriginalHotkey.DisplayText}; Win32 error {Marshal.GetLastWin32Error()}.");
        return false;
    }

    private void HideOverlay(string reason)
    {
        if (!_overlayVisible || _exiting)
        {
            return;
        }
        _overlayVisible = false;
        _queryGeneration++;
        _shortQueryDeferrer.Cancel();
        InvalidateSearches();
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
        _pipe.Emit(new { @event = "hidden", reason });
        AppLog.Write($"Hide: {reason}.");
    }

    private nint GetCursorMonitor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        return NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
    }

    private void MoveToMonitor(nint monitor)
    {
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var windowRect = GetWindowRect();
        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        AppWindow.Move(new PointInt32(
            info.Work.Left + ((info.Work.Right - info.Work.Left - width) / 2),
            info.Work.Top + ((info.Work.Bottom - info.Work.Top - height) / 2)));
    }

    private void QueueVisibleReadyAfterRender()
    {
        void OnRendering(object? sender, object args)
        {
            CompositionTarget.Rendering -= OnRendering;
            QueryBox.Focus(FocusState.Programmatic);
            NativeMethods.DwmFlush();
            NativeMethods.GetWindowRect(_windowHandle, out var rect);
            var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = rect.Left, Y = rect.Top }, NativeMethods.MonitorDefaultToNearest);
            CenterActualWindowOnMonitor(monitor, rect);
            NativeMethods.DwmFlush();
            NativeMethods.GetWindowRect(_windowHandle, out rect);
            _pipe.Emit(new { @event = "visible-ready", hwnd = (long)_windowHandle, focusHwnd = (long)NativeMethods.GetFocus(), queryHasKeyboardFocus = ReferenceEquals(FocusManager.GetFocusedElement(QueryBox.XamlRoot), QueryBox), layout = _overlayMode.ToString(), windowDpi = GetCurrentWindowDpi(), windowLeft = rect.Left, windowTop = rect.Top, windowWidth = rect.Right - rect.Left, windowHeight = rect.Bottom - rect.Top });
            _pipe.Emit(new { @event = "shell-icons-ready" });
            AppLog.Write("Visible-ready.");
            _visibleReadyCount++;
            if (_searchPerformanceScenario is not null && _visibleReadyCount == 1)
            {
                _ = RunSearchPerformanceScenarioAsync(_searchPerformanceScenario);
            }
            if (_options.ExitAfterVisibleReadyCount == _visibleReadyCount) Dispose();
        }
        CompositionTarget.Rendering += OnRendering;
    }

    private async Task RunSearchPerformanceScenarioAsync(SearchPerformanceScenario scenario)
    {
        try
        {
            _searchTrace.RecordScenarioStarted(scenario.Id);
            foreach (var warmupQuery in scenario.WarmupQueries)
            {
                await SubmitScenarioQueryAndWaitForRenderAsync(warmupQuery);
            }

            if (scenario.Queries.Count == 1)
            {
                await SubmitScenarioQueryAndWaitForRenderAsync(scenario.Queries[0]);
            }
            else
            {
                for (var index = 0; index < scenario.Queries.Count; index++)
                {
                    QueryBox.Text = scenario.Queries[index];
                    if (index < scenario.Queries.Count - 1)
                    {
                        await Task.Delay(scenario.InterQueryDelayMilliseconds);
                    }
                }

                await WaitForScenarioRenderAsync(_queryGeneration);
            }

            _searchTrace.RecordScenarioCompleted();
        }
        catch (Exception exception)
        {
            _searchTrace.RecordScenarioFailed();
            AppLog.Write("Search performance scenario failed.", exception);
        }
        finally
        {
            _exitApplication();
        }
    }

    private async Task SubmitScenarioQueryAndWaitForRenderAsync(string query)
    {
        QueryBox.Text = string.Empty;
        QueryBox.Text = query;
        await WaitForScenarioRenderAsync(_queryGeneration);
    }

    private Task WaitForScenarioRenderAsync(long uiGeneration)
    {
        _searchPerformanceAwaitedUiGeneration = uiGeneration;
        _searchPerformanceRenderCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return _searchPerformanceRenderCompletion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private void CenterActualWindowOnMonitor(nint monitor, NativeMethods.Rect windowRect)
    {
        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info)) return;
        var width = windowRect.Right - windowRect.Left;
        var height = windowRect.Bottom - windowRect.Top;
        AppWindow.Move(new PointInt32(info.Work.Left + ((info.Work.Right - info.Work.Left - width) / 2), info.Work.Top + ((info.Work.Bottom - info.Work.Top - height) / 2)));
    }

    private void OnQueryChanged(object sender, TextChangedEventArgs args)
    {
        ApplySearch();
    }

    private void ApplySearch()
    {
        _queryGeneration++;
        _shortQueryDeferrer.Cancel();
        var query = QueryBox?.Text?.Trim() ?? string.Empty;
        _searchTrace.RecordInput(_queryGeneration, query.Length);
        if (!_settingsDialogActive)
        {
            ApplyOverlayMode(QuickSearchOverlayLayout.ForQuery(query), recenter: true);
        }
        if (string.IsNullOrWhiteSpace(query))
        {
            InvalidateSearches();
            ClearResults();
            return;
        }

        if (!_searchRuntime.HasSources())
        {
            InvalidateSearches();
            ClearResults();
            StatusText.Text = "No search sources configured.";
            _pipe.Emit(new { @event = "query-changed", query, resultCount = 0 });
            return;
        }

        InvalidateSearches();
        ClearResults();
        StatusText.Text = "Searching…";

        if (query.Length is 1 or 2)
        {
            _shortQueryDeferrer.Schedule(_queryGeneration, query);
            _searchTrace.RecordShortQueryDeferred(_queryGeneration, query.Length);
            AppLog.Write($"Search deferred length={query.Length} delayMs={QuickSearchInputPolicy.ShortQueryDefer.TotalMilliseconds:F0}.");
            return;
        }

        StartSearch(query);
    }

    private void OnShortQueryReady(long queryGeneration, string query)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _searchTrace.RecordShortQueryReleased(queryGeneration, query.Length);
            if (_exiting ||
                !_overlayVisible ||
                queryGeneration != _queryGeneration ||
                !string.Equals(QueryBox.Text.Trim(), query, StringComparison.Ordinal))
            {
                return;
            }

            StartSearch(query);
        });
    }

    private void StartSearch(string query)
    {
        var coordinator = query.Length is 1 or 2
            ? _shortQuerySearchCoordinator
            : _interactiveSearchCoordinator;
        var generation = coordinator.Request(query, _queryGeneration);
        AppLog.Write($"Search request lane={coordinator.Lane} generation={generation} length={query.Length}.");
    }

    private void OnSearchCompleted(SearchCompletion completion)
    {
        _searchTrace.RecordCompletionDispatch(completion);
        DispatcherQueue.TryEnqueue(async () =>
        {
            _searchTrace.RecordUiDispatchStarted(completion);
            if (_exiting ||
                !_overlayVisible ||
                !completion.IsCurrent ||
                completion.UiGeneration != _queryGeneration)
            {
                AppLog.Write($"Search discarded generation={completion.Generation} current={completion.IsCurrent}.");
                return;
            }

            if (completion.Error is not null)
            {
                ClearResults();
                StatusText.Text = "A search source is unavailable or not search-ready.";
                AppLog.Write($"Search failed generation={completion.Generation}.", completion.Error);
                return;
            }

            var mappingStartedTimestamp = Stopwatch.GetTimestamp();
            var items = completion.Results!.Select(SearchResultPresentation.Map).ToArray();
            _searchTrace.RecordResultMapping(
                completion.UiGeneration,
                completion.Generation,
                items.Length,
                Stopwatch.GetElapsedTime(mappingStartedTimestamp));

            var resultApplyStartedTimestamp = Stopwatch.GetTimestamp();
            _visibleResults.Clear();
            foreach (var item in items) _visibleResults.Add(item);
            _searchTrace.RecordResultApply(
                completion.UiGeneration,
                completion.Generation,
                _visibleResults.Count,
                Stopwatch.GetElapsedTime(resultApplyStartedTimestamp));

            var selectionStartedTimestamp = Stopwatch.GetTimestamp();
            SetSelection(_visibleResults.Count > 0 ? 0 : -1);
            _searchTrace.RecordSelectionAndScroll(
                completion.UiGeneration,
                completion.Generation,
                Stopwatch.GetElapsedTime(selectionStartedTimestamp));

            var sourceStatusStartedTimestamp = Stopwatch.GetTimestamp();
            var sourceStatusNotice = _searchRuntime.GetSourceStatusNotice();
            StatusText.Text = _visibleResults.Count == 0
                ? sourceStatusNotice is null ? "No results." : $"No results. {sourceStatusNotice}"
                : sourceStatusNotice ?? string.Empty;
            _searchTrace.RecordSourceStatus(
                completion.UiGeneration,
                completion.Generation,
                Stopwatch.GetElapsedTime(sourceStatusStartedTimestamp));
            _pipe.Emit(new { @event = "query-changed", query = QueryBox.Text.Trim(), resultCount = _visibleResults.Count });
            AppLog.Write($"Search completed generation={completion.Generation} durationMs={completion.Duration.TotalMilliseconds:F1} results={items.Length}.");
            QueueFirstTextRender(completion, items.Length);
            for (var index = 0; index < items.Length; index++)
            {
                await LoadIconAsync(items[index], completion, index);
            }
        });
    }

    private void QueueFirstTextRender(SearchCompletion completion, int resultCount)
    {
        if (!_searchTrace.IsEnabled)
        {
            return;
        }

        void OnRendering(object? sender, object args)
        {
            CompositionTarget.Rendering -= OnRendering;
            NativeMethods.DwmFlush();
            _searchTrace.RecordFirstTextRender(completion.UiGeneration, completion.Generation, resultCount);
            if (_searchPerformanceAwaitedUiGeneration == completion.UiGeneration)
            {
                _searchPerformanceRenderCompletion?.TrySetResult();
                _searchPerformanceAwaitedUiGeneration = null;
                _searchPerformanceRenderCompletion = null;
            }
        }

        CompositionTarget.Rendering += OnRendering;
    }

    private void ClearResults()
    {
        _selectedResultIndex = -1;
        _visibleResults.Clear();
        StatusText.Text = string.Empty;
    }

    private void OnSourcesChanged()
    {
        if (_exiting)
        {
            return;
        }

        InvalidateSearches();
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_exiting) return;
            if (_overlayVisible)
            {
                ApplySearch();
            }
            else
            {
                _queryGeneration++;
                _shortQueryDeferrer.Cancel();
                ClearResults();
            }
        });
    }

    private void InvalidateSearches()
    {
        _interactiveSearchCoordinator.Invalidate();
        _shortQuerySearchCoordinator.Invalidate();
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
            case VirtualKey.Home:
                MoveToBoundary(last: false);
                args.Handled = true;
                break;
            case VirtualKey.End:
                MoveToBoundary(last: true);
                args.Handled = true;
                break;
            case VirtualKey.Enter when GetSelectedResult() is ResultItem result:
                OpenSelectedResult(result);
                args.Handled = true;
                break;
            case VirtualKey.Escape:
                HideOverlay("escape");
                args.Handled = true;
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (ResultSelection.TryGetMoveTarget(_visibleResults.Count, _selectedResultIndex, delta, out var target))
        {
            SetSelection(target);
        }
    }

    private void MoveToBoundary(bool last)
    {
        if (ResultSelection.TryGetBoundaryTarget(_visibleResults.Count, last, out var target))
        {
            SetSelection(target);
        }
    }

    private void SetSelection(int index)
    {
        if (index < 0 || index >= _visibleResults.Count)
        {
            _selectedResultIndex = -1;
            return;
        }

        foreach (var item in _visibleResults)
        {
            item.IsKeyboardSelected = false;
        }

        var selected = _visibleResults[index];
        selected.IsKeyboardSelected = true;
        _selectedResultIndex = index;
        ResultsList.ScrollIntoView(selected);
        _pipe.Emit(new { @event = "selection-changed", index, name = selected.Title });
    }

    private ResultItem? GetSelectedResult()
    {
        return _selectedResultIndex >= 0 && _selectedResultIndex < _visibleResults.Count
            ? _visibleResults[_selectedResultIndex]
            : null;
    }

    private void OnResultItemClick(object sender, ItemClickEventArgs args)
    {
        if (args.ClickedItem is not ResultItem result)
        {
            return;
        }

        QueryBox.Focus(FocusState.Programmatic);
        AppLog.Write($"Result click: {result.Title}.");
        OpenSelectedResult(result);
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs args)
    {
        ShowSettings();
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated &&
            QuickSearchLifecycle.ShouldRestoreHotkeyOnSettingsDeactivation(_settingsDialogActive, _hotkeyCaptureSession.IsActive))
        {
            RestoreHotkeyAfterCapture();
            return;
        }

        if (args.WindowActivationState != WindowActivationState.Deactivated ||
            !QuickSearchLifecycle.ShouldHideOnDeactivation(_overlayVisible, _settingsDialogActive, _exiting))
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (QuickSearchLifecycle.ShouldHideOnDeactivation(_overlayVisible, _settingsDialogActive, _exiting) &&
                NativeMethods.GetForegroundWindow() != _windowHandle)
            {
                HideOverlay("deactivated");
            }
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (!_exiting)
        {
            _exitApplication();
        }
    }

    private void ApplyTheme(string theme)
    {
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
    }

    private async void OpenSelectedResult(ResultItem result)
    {
        try
        {
            await Task.Run(() => _searchService.Open(result.Action));
            _pipe.Emit(new { @event = "confirmed", name = result.Title });
            AppLog.Write("Open succeeded.");
            HideOverlay("open-success");
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not open the selected item.";
            QueryBox.Focus(FocusState.Programmatic);
            _pipe.Emit(new { @event = "open-failed", queryHasKeyboardFocus = ReferenceEquals(FocusManager.GetFocusedElement(QueryBox.XamlRoot), QueryBox) });
            AppLog.Write("Open failed.", exception);
        }
    }

    private Task ApplyOverlayModeAsync(QuickSearchOverlayMode mode, bool forceResize = false)
    {
        if (_overlayMode == mode && !forceResize)
        {
            return Task.CompletedTask;
        }

        var transition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplyOverlayMode(mode, recenter: true, forceResize: forceResize, afterResize: () => transition.TrySetResult());
        return transition.Task;
    }

    private Task ApplySettingsHostLayoutAsync()
    {
        var transition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ApplyOverlayMode(QuickSearchOverlayMode.Expanded, recenter: false, animateExpandedContent: false);
        var physicalSize = QuickSearchOverlayLayout.LogicalSettingsSizeToPhysical(GetCurrentWindowDpi());
        AppWindow.Resize(new SizeInt32(physicalSize.Width, physicalSize.Height));
        if (!DispatcherQueue.TryEnqueue(() => CenterOnCursorMonitorAfterResize(() => transition.TrySetResult())))
        {
            transition.TrySetResult();
        }
        return transition.Task;
    }

    private void ApplyOverlayMode(QuickSearchOverlayMode mode, bool recenter, bool forceResize = false, Action? afterResize = null, bool animateExpandedContent = true)
    {
        if (_overlayMode == mode && !forceResize)
        {
            afterResize?.Invoke();
            return;
        }

        _overlayMode = mode;
        var isExpanded = mode == QuickSearchOverlayMode.Expanded;
        ExpandedContent.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (isExpanded && animateExpandedContent)
        {
            StartExpandedContentTransition();
        }
        var physicalSize = QuickSearchOverlayLayout.LogicalSizeToPhysical(mode, GetCurrentWindowDpi());
        AppWindow.Resize(new SizeInt32(physicalSize.Width, physicalSize.Height));

        if (recenter && _windowHandle != 0)
        {
            if (!DispatcherQueue.TryEnqueue(() => CenterOnCursorMonitorAfterResize(afterResize)))
            {
                afterResize?.Invoke();
            }

            return;
        }

        afterResize?.Invoke();
    }

    private void StartExpandedContentTransition()
    {
        ExpandedContent.Opacity = 0;
        ExpandedContentTransform.Y = 6;
        var duration = new Duration(TimeSpan.FromMilliseconds(ExpandedContentTransitionMilliseconds));
        var storyboard = new Storyboard();
        var opacity = new DoubleAnimation { To = 1, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(opacity, ExpandedContent);
        Storyboard.SetTargetProperty(opacity, "Opacity");
        storyboard.Children.Add(opacity);
        var translation = new DoubleAnimation { To = 0, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(translation, ExpandedContentTransform);
        Storyboard.SetTargetProperty(translation, "Y");
        storyboard.Children.Add(translation);
        storyboard.Begin();
    }

    private void CenterOnCursorMonitorAfterResize(Action? afterResize = null)
    {
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.Point { X = GetWindowRect().Left, Y = GetWindowRect().Top }, NativeMethods.MonitorDefaultToNearest);
        NativeMethods.DwmFlush();
        var rect = GetWindowRect();
        CenterActualWindowOnMonitor(monitor, rect);
        NativeMethods.DwmFlush();
        rect = GetWindowRect();
        _pipe.Emit(new { @event = "layout-changed", layout = _overlayMode.ToString(), windowDpi = GetCurrentWindowDpi(), windowLeft = rect.Left, windowTop = rect.Top, windowWidth = rect.Right - rect.Left, windowHeight = rect.Bottom - rect.Top });
        afterResize?.Invoke();
    }

    private void EmitSettingsLayoutEvent(string eventName)
    {
        NativeMethods.DwmFlush();
        var rect = GetWindowRect();
        _pipe.Emit(new
        {
            @event = eventName,
            layout = _overlayMode.ToString(),
            windowDpi = GetCurrentWindowDpi(),
            windowLeft = rect.Left,
            windowTop = rect.Top,
            windowWidth = rect.Right - rect.Left,
            windowHeight = rect.Bottom - rect.Top,
            queryHasKeyboardFocus = ReferenceEquals(FocusManager.GetFocusedElement(QueryBox.XamlRoot), QueryBox)
        });
    }

    private uint GetCurrentWindowDpi()
    {
        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        return dpi == 0 ? 96u : dpi;
    }

    private NativeMethods.Rect GetWindowRect()
    {
        NativeMethods.GetWindowRect(_windowHandle, out var rect);
        return rect;
    }

    private async Task LoadIconAsync(ResultItem item, SearchCompletion completion, int resultIndex)
    {
        if (string.IsNullOrWhiteSpace(item.IconKey))
        {
            return;
        }

        try
        {
            var iconStartedTimestamp = Stopwatch.GetTimestamp();
            _searchTrace.RecordIconStarted(completion.UiGeneration, completion.Generation, resultIndex);
            var icon = await _shellIcons.LoadAsync(item.IconKey);
            var applied = !_exiting && _overlayVisible && _visibleResults.Contains(item);
            if (applied)
            {
                item.Icon = icon;
            }
            _searchTrace.RecordIconCompleted(
                completion.UiGeneration,
                completion.Generation,
                resultIndex,
                Stopwatch.GetElapsedTime(iconStartedTimestamp),
                applied);
        }
        catch (Exception exception)
        {
            if (!_shellIconFailureLogged)
            {
                _shellIconFailureLogged = true;
                AppLog.Write("Shell icon load failed; fallback icons remain in use.", exception);
            }
        }
    }
}
