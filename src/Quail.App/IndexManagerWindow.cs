using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quail.FileSystem;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Quail.App;

internal sealed class IndexManagerWindow : Window
{
    private readonly IndexCatalogController _catalog;
    private readonly IndexOperationCoordinator _operations;
    private readonly StackPanel _content = new() { Spacing = 12, Padding = new Thickness(18) };
    private readonly ScrollViewer _root;
    private readonly Grid _windowRoot;
    private bool _initialSizeApplied;
    private bool _closed;
    private string? _message;

    public IndexManagerWindow(IndexCatalogController catalog, IndexOperationCoordinator operations, string theme)
    {
        _catalog = catalog;
        _operations = operations;
        Title = "Quail Indexes";
        _root = new ScrollViewer
        {
            Content = _content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            RequestedTheme = theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            }
        };
        _windowRoot = new Grid
        {
            Style = Application.Current.Resources["QuailIndexRootStyle"] as Style,
            RequestedTheme = _root.RequestedTheme
        };
        _windowRoot.Children.Add(_root);
        Content = _windowRoot;
        ApplyNativeTitleBarTheme(theme);
        _operations.Changed += OnOperationsChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _operations.Changed -= OnOperationsChanged;
            ClosedByUser?.Invoke();
        };
        Render();
    }

    public event Action? ClosedByUser;

    public void ShowMessage(string message)
    {
        _message = message;
        Render();
        ActivateManager();
    }

    public void ActivateManager()
    {
        Activate();
        if (!_initialSizeApplied)
        {
            DispatcherQueue.TryEnqueue(ApplyInitialSize);
        }
    }

    private void ApplyInitialSize()
    {
        if (_initialSizeApplied)
        {
            return;
        }
        var dpi = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var size = IndexManagerWindowLayout.InitialSizeToPhysical(dpi == 0 ? 96u : dpi);
        AppWindow.Resize(new SizeInt32(size.Width, size.Height));
        _initialSizeApplied = true;
    }

    private void Render(string? message = null)
    {
        _content.Children.Clear();
        _content.Children.Add(new TextBlock { Text = "Indexes", FontSize = 26, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        _content.Children.Add(Description("Manage the local volumes that Quick Search can use. Building and refreshing an index may request administrator approval."));
        if (_operations.HasRunningOperations)
        {
            var progress = new StackPanel { Spacing = 10 };
            progress.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5 });
            foreach (var operation in _operations.Running)
            {
                progress.Children.Add(Description($"{operation.Operation} is running for {operation.VolumeIdentity}."));
            }
            _content.Children.Add(Card(progress));
        }
        if (_catalog.LoadError is not null)
        {
            _content.Children.Add(MessageCard(_catalog.LoadError));
        }
        _content.Children.Add(new TextBlock { Text = "Configured indexes", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        if (_catalog.Entries.Count == 0)
        {
            _content.Children.Add(Description("No local indexes are configured."));
        }
        foreach (var entry in _catalog.Entries)
        {
            _content.Children.Add(CreateEntry(entry));
        }

        _content.Children.Add(new TextBlock { Text = "Add local volume", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        var volumes = VolumeDiscovery.Discover().Where(volume => !_catalog.IsConfigured(volume.VolumeIdentity)).ToArray();
        if (volumes.Length == 0)
        {
            _content.Children.Add(Description("No additional supported fixed NTFS volumes were found."));
        }
        foreach (var volume in volumes)
        {
            _content.Children.Add(CreateAddVolumeCard(volume));
        }
        if (message is not null)
        {
            _message = message;
        }
        if (_message is not null)
        {
            _content.Children.Add(MessageCard(_message));
        }
    }

    private UIElement CreateEntry(IndexCatalogEntry entry)
    {
        var status = new IndexStore(entry.DatabasePath).GetStatus();
        var availability = IndexManagerActionAvailability.For(status.State);
        var panel = new StackPanel
        {
            Spacing = 10
        };
        panel.Children.Add(new TextBlock { Text = entry.MountPoint, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var volumePresentation = GetVolumePresentation(entry);
        panel.Children.Add(new TextBlock { Text = volumePresentation.Headline ?? StateLabel(status.State), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Description(volumePresentation.Detail ?? StatusDetails(status)));
        panel.Children.Add(Description(entry.EnabledForSearch ? "Available to Quick Search" : "Not available to Quick Search"));

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };
        AddAction(
            actions,
            availability.PrimaryOperation.ToString(),
            () => RunOperationAsync(availability.PrimaryOperation, entry),
            primary: true,
            enabled: !_operations.HasRunningOperations);
        if (availability.ShowRebuild)
        {
            AddAction(
                actions,
                "Rebuild",
                () => RunOperationAsync(AdminIndexOperation.Rebuild, entry),
                enabled: !_operations.HasRunningOperations);
        }
        if (availability.ShowRefresh)
        {
            AddAction(
                actions,
                "Refresh",
                () => RunOperationAsync(AdminIndexOperation.Refresh, entry),
                enabled: availability.RefreshAvailable && !_operations.HasRunningOperations);
        }
        AddAction(actions, entry.EnabledForSearch ? "Disable" : "Enable", () => RunUiActionAsync(() => _catalog.SetEnabledAsync(entry.VolumeIdentity, !entry.EnabledForSearch)), enabled: !_operations.HasRunningOperations);
        AddAction(
            actions,
            "Remove",
            () => RunUiActionAsync(
                () => _catalog.RemoveAsync(entry.VolumeIdentity),
                "Index configuration removed. The database remains on disk."),
            tertiary: true,
            enabled: !_operations.HasRunningOperations);
        panel.Children.Add(actions);
        return Card(panel);
    }

    private UIElement CreateAddVolumeCard(DiscoveredVolume volume)
    {
        var panel = new StackPanel
        {
            Spacing = 8
        };
        panel.Children.Add(new TextBlock { Text = DisplayVolume(volume.MountPoint, volume.Label), FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(Description("Fixed NTFS volume"));
        var add = new Button
        {
            Content = "Add volume",
            Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsEnabled = !_operations.HasRunningOperations
        };
        add.Click += async (_, _) =>
        {
            await RunUiActionAsync(
                () => _catalog.AddAsync(new VolumeDescriptor(
                    volume.VolumeIdentity,
                    volume.MountPoint,
                    "NTFS",
                    volume.Label)),
                "Volume added. Build it before it can be searched.");
        };
        panel.Children.Add(add);
        return Card(panel);
    }

    private void AddAction(StackPanel panel, string text, Func<Task> action, bool primary = false, bool tertiary = false, bool enabled = true)
    {
        var button = new Button { Content = text, IsEnabled = enabled };
        if (primary)
        {
            button.Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style;
        }
        else if (tertiary)
        {
            button.Style = Application.Current.Resources["QuailTertiaryActionButtonStyle"] as Style;
        }
        button.Click += async (_, _) =>
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                Render(exception.Message);
            }
        };
        panel.Children.Add(button);
    }

    private async Task RunOperationAsync(AdminIndexOperation operation, IndexCatalogEntry entry)
    {
        Render($"{operation} is running with administrator approval…");
        AdminOperationResult result;
        try
        {
            result = await _operations.StartAsync(operation, entry);
        }
        catch (Exception exception)
        {
            Render(exception.Message);
            return;
        }
        if (_closed)
        {
            return;
        }
        Render(result.Success ? result.RebuildRequired ? "Refresh completed: rebuild is required." : $"{operation} completed." : result.Detail ?? $"{operation} failed.");
    }

    private async Task RunUiActionAsync(Func<Task> action, string? successMessage = null)
    {
        try
        {
            await action();
            Render(successMessage);
        }
        catch (Exception exception)
        {
            Render(exception.Message);
        }
    }

    private void OnOperationsChanged()
    {
        if (!_closed)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_closed)
                {
                    Render();
                }
            });
        }
    }

    private void ApplyNativeTitleBarTheme(string theme)
    {
        var useDark = theme == "Dark" || theme == "System" && IsSystemDark();
        var value = useDark ? 1u : 0u;
        var result = NativeMethods.DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(uint));
        if (result != 0)
        {
            AppLog.Write(
                $"DwmSetWindowAttribute(DWMWA_USE_IMMERSIVE_DARK_MODE) failed with HRESULT 0x{result:X8}.");
        }
    }

    private static bool IsSystemDark()
    {
        var color = new UISettings().GetColorValue(UIColorType.Background);
        return color.R + color.G + color.B < 384;
    }

    private static (string? Headline, string? Detail) GetVolumePresentation(IndexCatalogEntry entry)
    {
        try
        {
            var current = NtfsVolume.Validate(entry.MountPoint);
            return string.Equals(current.StableIdentity, entry.VolumeIdentity, StringComparison.OrdinalIgnoreCase)
                ? (null, null)
                : ("Volume mismatch", "The mounted volume no longer matches this configuration. Reconfigure this entry.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return ("Volume unavailable", "The configured volume is unavailable or could not be validated.");
        }
    }

    private static string StatusDetails(IndexStatus status)
    {
        var metadata = status.State == IndexState.Complete ? $"{status.RecordCount:N0} records. Last refreshed: {status.LastRefreshedUtc?.ToString("O") ?? "unknown"}." : string.Empty;
        return string.Join(" ", new[] { metadata, status.Detail, IndexFreshnessPolicy.Describe(status, DateTimeOffset.UtcNow) }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string StateLabel(IndexState state) => state switch
    {
        IndexState.Absent => "Not built",
        IndexState.Complete => "Ready",
        IndexState.RebuildRequired => "Rebuild required",
        IndexState.Incomplete => "Incomplete",
        _ => "Operational error"
    };

    private static string DisplayVolume(string mount, string label)
    {
        return string.IsNullOrWhiteSpace(label) ? mount : $"{mount} ({label})";
    }

    private static Border Card(UIElement content)
    {
        return new Border
        {
            Style = Application.Current.Resources["QuailIndexCardStyle"] as Style,
            Child = content
        };
    }

    private static Border MessageCard(string message) => Card(Description(message));

    private static TextBlock Description(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["QuailSecondaryTextStyle"] as Style
        };
    }
}
