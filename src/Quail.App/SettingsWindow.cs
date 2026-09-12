using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Quail.FileSystem;
using Windows.System;
using Windows.UI;
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
    private string _theme = "System";
    private NavigationView? _navigation;
    private ShellSettings _settings;
    private TextBox? _hotkeyBox;
    private ComboBox? _themeBox;
    private ToggleSwitch? _startupToggle;
    private TextBlock? _generalError;
    private bool _closing;
    private bool _initialSizeApplied;
    private nint _windowHandle;
    private nint _applicationSmallIcon;
    private nint _applicationLargeIcon;

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
        _windowHandle = WindowNative.GetWindowHandle(this);
        _applicationSmallIcon = BrandingAssets.CreateApplicationSmallIcon();
        _applicationLargeIcon = BrandingAssets.CreateApplicationLargeIcon();
        NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, NativeMethods.IconSmall, _applicationSmallIcon);
        NativeMethods.SendMessage(_windowHandle, NativeMethods.WmSetIcon, NativeMethods.IconBig, _applicationLargeIcon);
        ApplyTheme(settings.Theme);
        _windowRoot.ActualThemeChanged += (_, _) =>
        {
            if (_theme == "System" && !_closing)
            {
                ApplyNativeTitleBarTheme(_windowRoot.ActualTheme == ElementTheme.Dark);
            }
        };
        _operations.Changed += OnOperationsChanged;
        Closed += (_, _) =>
        {
            RestoreHotkeyForLifecycle(SettingsWindowLifecycleEvent.Closed);
            _closing = true;
            _operations.Changed -= OnOperationsChanged;
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
        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        var size = SettingsWindowLayout.InitialSizeToPhysical(dpi == 0 ? 96u : dpi);
        AppWindow.Resize(new SizeInt32(size.Width, size.Height));
        if (NativeMethods.GetCursorPos(out var cursor))
        {
            var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
            var info = new NativeMethods.MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
            if (monitor != 0 && NativeMethods.GetMonitorInfo(monitor, ref info) &&
                NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
            {
                var position = QuickSearchOverlayLayout.CenterInWorkArea(
                    info.Work.Left,
                    info.Work.Top,
                    info.Work.Right - info.Work.Left,
                    info.Work.Bottom - info.Work.Top,
                    new PhysicalSize(windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top));
                AppWindow.Move(new PointInt32(position.X, position.Y));
            }
        }
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
            IsPaneToggleButtonVisible = false,
            OpenPaneLength = 228,
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
        _startupToggle = new ToggleSwitch { IsOn = _startup.IsEnabled, VerticalAlignment = VerticalAlignment.Center };
        _startupToggle.Toggled += (_, _) =>
        {
            var error = _startup.SetEnabled(_startupToggle.IsOn);
            _startupToggle.IsOn = _startup.IsEnabled;
            ShowGeneralError(error);
        };
        panel.Children.Add(FeatureCard("", "Launch at startup", "Start Quail with Windows so Quick Search is ready when you need it.", _startupToggle));

        _hotkeyBox = new TextBox { Text = _settings.Hotkey, IsReadOnly = true, MinWidth = 180, Style = Application.Current.Resources["QuailCompactTextBoxStyle"] as Style };
        _hotkeyBox.KeyDown += OnHotkeyKeyDown;
        _hotkeyBox.LostFocus += (_, _) => RestoreHotkey();
        var changeHotkey = new Button { Content = "Change…", Style = Application.Current.Resources["QuailSecondaryActionButtonStyle"] as Style };
        changeHotkey.Click += (_, _) => _hotkeyBox.Focus(FocusState.Programmatic);
        var hotkeyControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        hotkeyControls.Children.Add(_hotkeyBox);
        hotkeyControls.Children.Add(changeHotkey);
        panel.Children.Add(FeatureCard("", "Quick Search hotkey", "Use this shortcut from anywhere to open Quick Search.", hotkeyControls));

        _themeBox = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = _settings.Theme, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(FeatureCard("", "Theme", "Choose how Quail follows the system appearance.", _themeBox));

        var save = new Button { Content = "Save", HorizontalAlignment = HorizontalAlignment.Right, Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style };
        save.Click += async (_, _) => await SaveGeneralAsync();
        panel.Children.Add(save);
        _generalError = Description(string.Empty);
        _generalError.Visibility = Visibility.Collapsed;
        panel.Children.Add(_generalError);
        ShowGeneralError(_initialGeneralError);
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    private static Border FeatureCard(string glyph, string title, string description, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new FontIcon { Glyph = glyph, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 22, Foreground = Application.Current.Resources["QuailIconBrush"] as Microsoft.UI.Xaml.Media.Brush, VerticalAlignment = VerticalAlignment.Center });
        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, FontSize = 16 });
        copy.Children.Add(Description(description));
        Grid.SetColumn(copy, 1);
        row.Children.Add(copy);
        Grid.SetColumn(control, 2);
        row.Children.Add(control);
        return Card(row);
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
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        heading.Children.Add(new SymbolIcon(Symbol.Folder) { VerticalAlignment = VerticalAlignment.Center });
        heading.Children.Add(new TextBlock { Text = entry.MountPoint, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(heading);
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
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 8, 0, 4) };
        identity.Children.Add(new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/quail-feather-A-gradient.svg")), Width = 34, Height = 34 });
        identity.Children.Add(new TextBlock { Text = "Quail", FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(identity);
        panel.Children.Add(Description($"Version {assembly?.Version?.ToString() ?? "unknown"}"));
        panel.Children.Add(new HyperlinkButton { Content = "GitHub repository", NavigateUri = new Uri("https://github.com/calorypher/Quail") });
        panel.Children.Add(new HyperlinkButton { Content = "MIT License", NavigateUri = new Uri("https://github.com/calorypher/Quail/blob/main/LICENSE") });
        return panel;
    }

    private void ApplyTheme(string theme)
    {
        _theme = theme;
        var requested = theme switch { "Light" => ElementTheme.Light, "Dark" => ElementTheme.Dark, _ => ElementTheme.Default };
        _windowRoot.RequestedTheme = requested;
        if (_navigation is not null) _navigation.RequestedTheme = requested;
        _content.RequestedTheme = requested;
        var useDark = theme == "Dark" || theme == "System" && IsSystemDark();
        ApplyNativeTitleBarTheme(useDark);
    }

    private void ApplyNativeTitleBarTheme(bool useDark)
    {
        var value = useDark ? 1u : 0u;
        _ = NativeMethods.DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(uint));
        ApplyCaptionButtonTheme(useDark);
    }

    private void ApplyCaptionButtonTheme(bool useDark)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var theme = CaptionButtonThemePolicy.ForEffectiveTheme(useDark);
        var foreground = Color.FromArgb(0xFF, theme.Red, theme.Green, theme.Blue);
        AppWindow.TitleBar.ButtonForegroundColor = foreground;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = foreground;
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
