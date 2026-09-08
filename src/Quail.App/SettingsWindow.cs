using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quail.FileSystem;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using Windows.Graphics;

namespace Quail.App;

internal sealed class SettingsWindow : Window
{
    private readonly IndexCatalogController _catalog;
    private readonly IndexOperationCoordinator _operations;
    private readonly IStartupRegistration _startup;
    private readonly Func<ShellSettings, Task<string?>> _saveSettings;
    private readonly Action _beginHotkeyCapture;
    private readonly Func<bool> _restoreHotkey;
    private readonly Func<bool> _isHotkeyCaptureActive;
    private readonly MaintenanceStateStore _maintenanceState = new();
    private readonly Frame _content = new();
    private readonly Grid _windowRoot = new();
    private NavigationView? _navigation;
    private ShellSettings _settings;
    private TextBox? _hotkeyBox;
    private ComboBox? _themeBox;
    private ToggleSwitch? _startupToggle;
    private TextBlock? _generalError;
    private bool _closing;
    private bool _initialSizeApplied;

    public SettingsWindow(
        IndexCatalogController catalog,
        IndexOperationCoordinator operations,
        ShellSettings settings,
        Func<ShellSettings, Task<string?>> saveSettings,
        Action beginHotkeyCapture,
        Func<bool> restoreHotkey,
        IStartupRegistration? startup = null,
        string? startupHotkeyError = null,
        Func<bool>? isHotkeyCaptureActive = null)
    {
        _catalog = catalog;
        _operations = operations;
        _settings = settings;
        _saveSettings = saveSettings;
        _beginHotkeyCapture = beginHotkeyCapture;
        _restoreHotkey = restoreHotkey;
        _isHotkeyCaptureActive = isHotkeyCaptureActive ?? (() => false);
        _startup = startup ?? new StartupRegistration();
        _initialGeneralError = startupHotkeyError;
        Title = "Quail Settings";
        _windowRoot.Style = Application.Current.Resources["QuailIndexRootStyle"] as Style;
        _windowRoot.Children.Add(CreateRoot());
        Content = _windowRoot;
        ApplyTheme(settings.Theme);
        _operations.Changed += OnOperationsChanged;
        Closed += (_, _) =>
        {
            RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent.Closed);
            _closing = true;
            _operations.Changed -= OnOperationsChanged;
            ClosedByUser?.Invoke();
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent.Deactivated);
            }
        };
    }

    public event Action? ClosedByUser;

    public void ActivateSettings()
    {
        Activate();
        if (!_initialSizeApplied)
        {
            DispatcherQueue.TryEnqueue(ApplyInitialSize);
        }
    }

    private string? _initialGeneralError;

    private void ApplyInitialSize()
    {
        if (_initialSizeApplied) return;
        var dpi = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var size = SettingsWindowLayout.InitialSizeToPhysical(dpi == 0 ? 96u : dpi);
        AppWindow.Resize(new SizeInt32(size.Width, size.Height));
        _initialSizeApplied = true;
    }

    public void ShowIndexingMessage(string message)
    {
        Navigate("indexing", message);
        ActivateSettings();
    }

    private UIElement CreateRoot()
    {
        _navigation = new NavigationView
        {
            IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
            IsSettingsVisible = false,
            PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
            Content = _content
        };
        _navigation.MenuItems.Add(new NavigationViewItem { Content = "General", Tag = "general", Icon = new SymbolIcon(Symbol.Setting) });
        _navigation.MenuItems.Add(new NavigationViewItem { Content = "Indexing", Tag = "indexing", Icon = new SymbolIcon(Symbol.Folder) });
        _navigation.MenuItems.Add(new NavigationViewItem { Content = "About", Tag = "about", Icon = new SymbolIcon(Symbol.Help) });
        _navigation.SelectionChanged += (_, args) =>
        {
            if (args.SelectedItem is NavigationViewItem item && item.Tag is string page)
            {
                RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent.Deactivated);
                Navigate(page);
            }
        };
        _navigation.SelectedItem = _navigation.MenuItems[0];
        Navigate("general");
        return _navigation;
    }

    private void Navigate(string page, string? message = null)
    {
        _content.Content = page switch
        {
            "indexing" => CreateIndexingPage(message),
            "about" => CreateAboutPage(),
            _ => CreateGeneralPage()
        };
    }

    private UIElement CreateGeneralPage()
    {
        var panel = PagePanel("General", "Choose how Quail starts, looks, and opens Quick Search.");
        _startupToggle = new ToggleSwitch { Header = "Launch Quail with Windows", IsOn = _startup.IsEnabled };
        _startupToggle.Toggled += (_, _) =>
        {
            var error = _startup.SetEnabled(_startupToggle.IsOn);
            _startupToggle.IsOn = _startup.IsEnabled;
            ShowGeneralError(error);
        };
        panel.Children.Add(Card(_startupToggle));
        _hotkeyBox = new TextBox { Header = "Quick Search hotkey", Text = _settings.Hotkey, IsReadOnly = true };
        _hotkeyBox.KeyDown += OnHotkeyKeyDown;
        _hotkeyBox.LostFocus += (_, _) => RestoreHotkey();
        _themeBox = new ComboBox { Header = "Theme", ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = _settings.Theme };
        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style };
        save.Click += async (_, _) => await SaveGeneralAsync();
        var settings = new StackPanel { Spacing = 12 };
        settings.Children.Add(_hotkeyBox);
        settings.Children.Add(_themeBox);
        settings.Children.Add(save);
        panel.Children.Add(Card(settings));
        _generalError = Description(string.Empty);
        _generalError.Visibility = Visibility.Collapsed;
        panel.Children.Add(_generalError);
        ShowGeneralError(_initialGeneralError);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private async Task SaveGeneralAsync()
    {
        var proposed = new ShellSettings(_hotkeyBox?.Text ?? _settings.Hotkey, _themeBox?.SelectedItem as string ?? _settings.Theme);
        var error = await _saveSettings(proposed);
        if (error is not null)
        {
            ShowGeneralError(error);
            return;
        }
        _settings = proposed.Normalize();
        _initialGeneralError = null;
        ApplyTheme(_settings.Theme);
        RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent.Saved);
        ShowGeneralError(null);
    }

    private void OnHotkeyKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Enter or VirtualKey.Escape) return;
        args.Handled = true;
        _beginHotkeyCapture();
        if (HotkeyCapture.TryCapture((uint)args.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift), IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows), out var displayText))
        {
            _hotkeyBox!.Text = displayText;
            ShowGeneralError(null);
        }
    }

    private void RestoreHotkey()
    {
        if (!SettingsHotkeyRestoreGuard.TryRestore(_restoreHotkey, out var error)) ShowGeneralError(error);
    }

    private void RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent @event)
    {
        if (SettingsWindowHotkeyLifecycle.ShouldRestoreHotkey(_isHotkeyCaptureActive(), @event)) RestoreHotkey();
    }

    private void ShowGeneralError(string? error)
    {
        if (_generalError is null) return;
        _generalError.Text = error ?? string.Empty;
        _generalError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private UIElement CreateIndexingPage(string? message)
    {
        var panel = PagePanel("Indexing", "The maintenance service keeps registered indexes current. Rebuild is only needed for recovery.");
        if (_catalog.LoadError is not null) panel.Children.Add(Description(_catalog.LoadError));
        foreach (var entry in _catalog.Entries) panel.Children.Add(CreateIndexCard(entry));
        var addHeader = new TextBlock { Text = "Add local volume", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(addHeader);
        foreach (var volume in VolumeDiscovery.Discover().Where(volume => !_catalog.IsConfigured(volume.StableIdentity)))
        {
            var add = new Button { Content = $"Add {volume.MountPoint}", HorizontalAlignment = HorizontalAlignment.Left };
            add.IsEnabled = !_operations.HasRunningOperations;
            add.Click += async (_, _) => await RunIndexActionAsync(async () =>
            {
                await _catalog.AddAsync(volume);
                Navigate("indexing", "Volume added. Build it before it can be searched.");
            });
            panel.Children.Add(add);
        }
        if (message is not null) panel.Children.Add(Description(message));
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private UIElement CreateIndexCard(IndexCatalogEntry entry)
    {
        var presentation = FileSystemIndexAdministration.GetPresentation(entry);
        var health = _maintenanceState.GetHealth(entry.VolumeIdentity);
        var registered = health is not null || _maintenanceState.IsRegistered(entry.VolumeIdentity);
        var panel = new StackPanel { Spacing = 8 };
        if (_operations.HasRunningOperations)
        {
            panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
            panel.Children.Add(Description("An index operation is in progress."));
        }
        panel.Children.Add(new TextBlock { Text = entry.MountPoint, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = HealthLabel(presentation.Status.State, health), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Description(presentation.VolumeDetail ?? HealthDetail(presentation.Status, health)));
        panel.Children.Add(Description(entry.EnabledForSearch ? "Enabled for Quick Search" : "Disabled for Quick Search"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var policy = IndexingActionPolicy.For(presentation.Status.State, registered, health?.State);
        if (policy.PrimaryOperation is { } primaryOperation) AddOperation(actions, primaryOperation, entry, primary: true, enableAfterBuild: policy.EnableAfterBuild);
        if (policy.ShowSecondaryRebuild) AddOperation(actions, AdminIndexOperation.Rebuild, entry, primary: false);
        AddAction(actions, entry.EnabledForSearch ? "Disable" : "Enable", async () => { await _catalog.SetEnabledAsync(entry.VolumeIdentity, !entry.EnabledForSearch); Navigate("indexing"); });
        AddOperation(actions, AdminIndexOperation.Unregister, entry, primary: false, label: "Remove");
        panel.Children.Add(actions);
        return Card(panel);
    }

    private void AddOperation(StackPanel panel, AdminIndexOperation operation, IndexCatalogEntry entry, bool primary, bool enableAfterBuild = false, string? label = null) =>
        AddAction(panel, label ?? operation.ToString(), async () =>
        {
            var result = await _operations.StartAsync(operation, entry, enableAfterBuild);
            Navigate("indexing", result.Success ? $"{operation} completed." : result.Detail ?? $"{operation} failed.");
        }, primary);

    private void AddAction(StackPanel panel, string text, Func<Task> action, bool primary = false)
    {
        var button = new Button { Content = text, IsEnabled = !_operations.HasRunningOperations };
        if (primary) button.Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style;
        button.Click += async (_, _) => await RunIndexActionAsync(action);
        panel.Children.Add(button);
    }

    private async Task RunIndexActionAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            Navigate("indexing", exception.Message);
        }
    }

    private UIElement CreateAboutPage()
    {
        var assembly = Assembly.GetEntryAssembly()?.GetName();
        var panel = PagePanel("About Quail", "A local-first Windows search application for your files.");
        panel.Children.Add(new TextBlock { Text = $"Version {assembly?.Version?.ToString() ?? "unknown"}" });
        panel.Children.Add(new HyperlinkButton { Content = "GitHub repository", NavigateUri = new Uri("https://github.com/calorypher/Quail") });
        panel.Children.Add(new HyperlinkButton { Content = "MIT License", NavigateUri = new Uri("https://github.com/calorypher/Quail/blob/main/LICENSE") });
        return panel;
    }

    private void ApplyTheme(string theme)
    {
        var requested = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _windowRoot.RequestedTheme = requested;
        if (_navigation is not null) _navigation.RequestedTheme = requested;
        _content.RequestedTheme = requested;
        var useDark = theme == "Dark" || theme == "System" && IsSystemDark();
        var value = useDark ? 1u : 0u;
        _ = NativeMethods.DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(uint));
    }

    private void OnOperationsChanged()
    {
        if (!_closing) DispatcherQueue.TryEnqueue(() => { if (!_closing) Navigate("indexing"); });
    }

    private static StackPanel PagePanel(string title, string description)
    {
        var panel = new StackPanel { Spacing = 12, Padding = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Description(description));
        return panel;
    }

    private static string HealthLabel(IndexState state, MaintenanceTargetHealth? health) => health?.State switch
    {
        MaintenanceHealthState.Healthy => "Up to date",
        MaintenanceHealthState.CatchingUp => "Updating",
        MaintenanceHealthState.Unavailable or MaintenanceHealthState.Retrying => "Temporarily unavailable",
        MaintenanceHealthState.RebuildRequired => "Rebuild required",
        MaintenanceHealthState.Error => "Maintenance error",
        _ => state switch { IndexState.RebuildRequired or IndexState.Incomplete => "Rebuild required", IndexState.Absent => "Not built", IndexState.Complete => "Maintenance unavailable", _ => "Maintenance error" }
    };

    private static string HealthDetail(IndexStatus status, MaintenanceTargetHealth? health) => health?.State switch
    {
        MaintenanceHealthState.Healthy => $"{status.RecordCount:N0} records. Last maintained: {health.LastSuccessfulMaintenanceUtc?.ToString("g") ?? status.LastRefreshedUtc?.ToString("g") ?? "unknown"}.",
        MaintenanceHealthState.CatchingUp => "Quail is applying filesystem changes.",
        MaintenanceHealthState.Unavailable or MaintenanceHealthState.Retrying => "Filesystem maintenance is temporarily unavailable and will retry.",
        MaintenanceHealthState.RebuildRequired => "Quail can no longer prove incremental index continuity. Rebuild the index to use it again.",
        MaintenanceHealthState.Error => "Filesystem maintenance encountered an error.",
        _ => status.State == IndexState.Complete ? $"{status.RecordCount:N0} records. Last maintained: {status.LastRefreshedUtc?.ToString("g") ?? "unknown"}." : "The index needs attention."
    };
    private static bool IsSystemDark()
    {
        var color = new UISettings().GetColorValue(UIColorType.Background);
        return color.R + color.G + color.B < 384;
    }
    private static bool IsDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;
    private static Border Card(UIElement content) => new() { Style = Application.Current.Resources["QuailSettingsCardStyle"] as Style, Child = content };
    private static TextBlock Description(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Style = Application.Current.Resources["QuailSecondaryTextStyle"] as Style };
}
