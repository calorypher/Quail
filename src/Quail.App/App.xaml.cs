using Microsoft.UI.Xaml;
using Quail.FileSystem;

namespace Quail.App;

public sealed partial class App : Application
{
    private readonly AppLaunchOptions _options;
    private readonly SettingsStore _settingsStore = new();
    private readonly IndexCatalogController _indexCatalog = new();
    private IndexOperationCoordinator? _indexOperations;
    private SearchRuntime? _searchRuntime;
    private SingleInstanceCoordinator? _singleInstance;
    private QuickSearchWindow? _window;
    private SettingsWindow? _settingsWindow;
    private FullSearchWindow? _fullSearchWindow;

    public App()
    {
        _options = StartupOptions.Current;
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _singleInstance = SingleInstanceCoordinator.Acquire();
            if (!_singleInstance.IsPrimary)
            {
                AppLog.Write("Secondary instance requested primary activation.");
                _singleInstance.ActivatePrimary();
                Exit();
                return;
            }

            var settings = await _settingsStore.LoadAsync();
            await _indexCatalog.LoadAsync();
            _indexOperations = new IndexOperationCoordinator(_indexCatalog);
            _searchRuntime = FileSystemSearchComposition.Create(_options, _indexCatalog);
            _window = new QuickSearchWindow(
                _options,
                _settingsStore,
                _searchRuntime,
                settings,
                ExitApplication,
                ShowSettings,
                ShowFullSearch,
                ActivateGlobalSearchSurface);
            _window.ThemeChanged += OnThemeChanged;
            _singleInstance.ActivationRequested += () => _window.DispatcherQueue.TryEnqueue(ActivateGlobalSearchSurface);
            await _window.InitializeAsync();
            AppLog.Write("Primary instance initialized.");
        }
        catch (Exception exception)
        {
            AppLog.Write("Fatal startup error.", exception);
            ExitApplication();
        }
    }

    private void ExitApplication()
    {
        if (_indexOperations?.HasRunningOperations == true)
        {
            ShowSettings();
            _settingsWindow?.ShowIndexingMessage("Finish the running index operation before exiting Quail.");
            return;
        }

        _settingsWindow?.Close();
        _settingsWindow = null;
        _fullSearchWindow?.Close();
        _fullSearchWindow = null;
        _window?.Dispose();
        _window = null;
        _searchRuntime?.Dispose();
        _searchRuntime = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        Exit();
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.ActivateSettings();
            return;
        }
        var window = _window ?? throw new InvalidOperationException("Quick Search is unavailable.");
        _settingsWindow = new SettingsWindow(
            _indexCatalog,
            _indexOperations ?? throw new InvalidOperationException("Index operation coordination is unavailable."),
            window.CurrentSettings,
            window.TryApplySettingsAsync,
            window.BeginHotkeyCapture,
            window.RestoreHotkeyAfterCapture,
            startupHotkeyError: window.StartupHotkeyError,
            isHotkeyCaptureActive: () => window.IsHotkeyCaptureActive);
        _settingsWindow.ClosedByUser += () => _settingsWindow = null;
        _settingsWindow.ActivateSettings();
    }

    private void ShowFullSearch(string query)
    {
        var quickSearch = _window ?? throw new InvalidOperationException("Quick Search is unavailable.");
        if (FullSearchLifecycle.ShouldCreateWindow(_fullSearchWindow is not null))
        {
            var initialMonitor = quickSearch.GetCurrentMonitor();
            _fullSearchWindow = new FullSearchWindow(
                _searchRuntime ?? throw new InvalidOperationException("Search is unavailable."),
                quickSearch.CurrentTheme,
                CollapseFullSearch,
                ShowSettings,
                initialMonitor);
            _fullSearchWindow.ClosedByUser += () => _fullSearchWindow = null;
        }

        _fullSearchWindow!.ActivateSearch(query, quickSearch.CurrentTheme);
    }

    private void CollapseFullSearch(string query)
    {
        if (FullSearchLifecycle.ShouldShowQuickSearch(FullSearchDismissKind.Collapse))
        {
            _window?.ShowOverlayWithQuery(query);
        }
    }

    private void ActivateGlobalSearchSurface()
    {
        var quickSearch = _window;
        if (quickSearch is null)
        {
            return;
        }

        var target = SearchSurfaceActivation.ForGlobalActivation(
            _fullSearchWindow?.IsSearchSurfaceVisible == true,
            _fullSearchWindow?.IsMinimized == true);
        if (target == SearchSurfaceActivationTarget.Full)
        {
            _fullSearchWindow!.ActivateSearch(_fullSearchWindow.Query, quickSearch.CurrentTheme);
            return;
        }

        quickSearch.ShowOverlay();
    }

    private void OnThemeChanged(string theme) => _fullSearchWindow?.ApplyTheme(theme);
}
