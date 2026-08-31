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
    private IndexManagerWindow? _indexManager;

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
            _window = new QuickSearchWindow(_options, _settingsStore, _searchRuntime, settings, ExitApplication, ShowIndexManager);
            _singleInstance.ActivationRequested += () => _window.DispatcherQueue.TryEnqueue(_window.ShowOverlay);
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
            ShowIndexManager();
            _indexManager?.ShowMessage("Finish the running index operation before exiting Quail.");
            return;
        }

        _indexManager?.Close();
        _indexManager = null;
        _window?.Dispose();
        _window = null;
        _searchRuntime?.Dispose();
        _searchRuntime = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        Exit();
    }

    private void ShowIndexManager()
    {
        if (_indexManager is not null)
        {
            _indexManager.ActivateManager();
            return;
        }
        _indexManager = new IndexManagerWindow(
            _indexCatalog,
            _indexOperations ?? throw new InvalidOperationException("Index operation coordination is unavailable."),
            _window?.CurrentTheme ?? "System");
        _indexManager.ClosedByUser += () => _indexManager = null;
        _indexManager.ActivateManager();
    }
}
