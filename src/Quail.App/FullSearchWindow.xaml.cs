using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quail.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using WinRT.Interop;

namespace Quail.App;

internal sealed partial class FullSearchWindow : Window
{
    private readonly SearchRuntime _searchRuntime;
    private readonly SearchApplicationService _searchService;
    private readonly LatestSearchCoordinator _searchCoordinator;
    private readonly Action<string> _collapse;
    private readonly Action _showSettings;
    private readonly nint _initialMonitor;
    private readonly ObservableCollection<FullSearchResultItem> _results = [];
    private nint _windowHandle;
    private long _uiGeneration;
    private bool _visible;
    private bool _closed;
    private bool _initialSizeApplied;
    private bool _clampingSize;
    private bool _controlsReady;
    private FullSearchSortField _sortField = FullSearchSortField.Relevance;
    private bool _sortDescending;

    public FullSearchWindow(
        SearchRuntime searchRuntime,
        string theme,
        Action<string> collapse,
        Action showSettings,
        nint initialMonitor)
    {
        _searchRuntime = searchRuntime ?? throw new ArgumentNullException(nameof(searchRuntime));
        _searchService = searchRuntime.Search;
        _collapse = collapse ?? throw new ArgumentNullException(nameof(collapse));
        _showSettings = showSettings ?? throw new ArgumentNullException(nameof(showSettings));
        _initialMonitor = initialMonitor;
        _searchCoordinator = LatestSearchCoordinator.ForRequests(
            (SearchRequest request) => _searchService.Search(request),
            lane: SearchExecutionLane.Interactive);
        _searchCoordinator.Completed += OnSearchCompleted;
        _searchRuntime.SourcesChanged += OnSourcesChanged;
        InitializeComponent();
        FeatherImage.Source = new SvgImageSource(new Uri("ms-appx:///Assets/quail-feather-A-gradient.svg"));
        ResultsList.ItemsSource = _results;
        Title = "Quail Full Search";
        _windowHandle = WindowNative.GetWindowHandle(this);
        ApplyTheme(theme);
        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
        _controlsReady = true;
    }

    public event Action? ClosedByUser;

    public string Query => QueryBox.Text;

    public void ActivateSearch(string query, string theme)
    {
        if (_closed)
        {
            return;
        }

        ApplyTheme(theme);
        _visible = true;
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        if (!_initialSizeApplied)
        {
            ApplyInitialSize();
        }
        Activate();
        NativeMethods.SetForegroundWindow(_windowHandle);

        var transferredQuery = FullSearchLifecycle.TransferQuery(query);
        var queryChanged = !string.Equals(QueryBox.Text, transferredQuery, StringComparison.Ordinal);
        QueryBox.Text = transferredQuery;
        QueryBox.SelectionStart = QueryBox.Text.Length;
        QueryBox.Focus(FocusState.Programmatic);
        if (!queryChanged)
        {
            ApplySearch();
        }
    }

    public void HideForCollapse()
    {
        if (_closed)
        {
            return;
        }

        _visible = false;
        _uiGeneration++;
        _searchCoordinator.Invalidate();
        NativeMethods.ShowWindow(_windowHandle, NativeMethods.SwHide);
    }

    public void ApplyTheme(string theme)
    {
        var requested = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        RootGrid.RequestedTheme = requested;
        var useDark = theme == "Dark" || theme == "System" && IsSystemDark();
        var value = useDark ? 1u : 0u;
        _ = NativeMethods.DwmSetWindowAttribute(_windowHandle, NativeMethods.DwmwaUseImmersiveDarkMode, ref value, sizeof(uint));
    }

    private void ApplyInitialSize()
    {
        if (_initialSizeApplied || _closed)
        {
            return;
        }

        MoveToInitialMonitor();
        NativeMethods.DwmFlush();
        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        var size = FullSearchWindowLayout.InitialSizeToPhysical(dpi == 0 ? 96u : dpi);
        AppWindow.Resize(new SizeInt32(size.Width, size.Height));
        CenterOnInitialMonitor();
        _initialSizeApplied = true;
    }

    private void MoveToInitialMonitor()
    {
        if (_initialMonitor == 0)
        {
            return;
        }

        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (NativeMethods.GetMonitorInfo(_initialMonitor, ref info))
        {
            AppWindow.Move(new PointInt32(info.Work.Left, info.Work.Top));
        }
    }

    private void CenterOnInitialMonitor()
    {
        if (_initialMonitor == 0)
        {
            return;
        }

        var info = new NativeMethods.MonitorInfo { Size = (uint)Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(_initialMonitor, ref info) ||
            !NativeMethods.GetWindowRect(_windowHandle, out var windowRect))
        {
            return;
        }

        var position = QuickSearchOverlayLayout.CenterInWorkArea(
            info.Work.Left,
            info.Work.Top,
            info.Work.Right - info.Work.Left,
            info.Work.Bottom - info.Work.Top,
            new PhysicalSize(windowRect.Right - windowRect.Left, windowRect.Bottom - windowRect.Top));
        AppWindow.Move(new PointInt32(position.X, position.Y));
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidSizeChange || _clampingSize || _closed)
        {
            return;
        }

        var dpi = NativeMethods.GetDpiForWindow(_windowHandle);
        var minimum = FullSearchWindowLayout.MinimumSizeToPhysical(dpi == 0 ? 96u : dpi);
        var current = sender.Size;
        if (current.Width >= minimum.Width && current.Height >= minimum.Height)
        {
            return;
        }

        _clampingSize = true;
        sender.Resize(new SizeInt32(
            Math.Max(current.Width, minimum.Width),
            Math.Max(current.Height, minimum.Height)));
        _clampingSize = false;
    }

    private void OnSearchInputChanged(object sender, object args)
    {
        if (_controlsReady)
        {
            ApplySearch();
        }
    }

    private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        OnSearchInputChanged(sender, args);

    private void OnDateChanged(CalendarDatePicker sender, CalendarDatePickerDateChangedEventArgs args) =>
        OnSearchInputChanged(sender, args);

    private void OnAddFilterClicked(object sender, RoutedEventArgs args)
    {
        AdvancedFiltersPanel.Visibility = AdvancedFiltersPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        AddFilterButton.Content = AdvancedFiltersPanel.Visibility == Visibility.Visible
            ? "Hide filters"
            : "Add filter";
    }

    private void OnRelevanceClicked(object sender, RoutedEventArgs args)
    {
        (_sortField, _sortDescending) = FullSearchSortInteraction.RestoreRelevance();
        UpdateSortPresentation();
        ApplySearch();
    }

    private void OnSortHeaderClicked(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string name } ||
            !Enum.TryParse<FullSearchSortField>(name, out var field))
        {
            return;
        }

        (_sortField, _sortDescending) = FullSearchSortInteraction.SelectColumn(
            _sortField,
            _sortDescending,
            field);
        UpdateSortPresentation();
        ApplySearch();
    }

    private void UpdateSortPresentation()
    {
        RelevanceButton.Content = _sortField == FullSearchSortField.Relevance ? "Relevance ✓" : "Relevance";
        NameHeaderButton.Content = HeaderLabel("Name", FullSearchSortField.Name);
        PathHeaderButton.Content = HeaderLabel("Path", FullSearchSortField.Path);
        SizeHeaderButton.Content = HeaderLabel("Size", FullSearchSortField.Size);
        ModifiedHeaderButton.Content = HeaderLabel("Modified", FullSearchSortField.Modified);
    }

    private string HeaderLabel(string label, FullSearchSortField field) =>
        _sortField == field ? $"{label} {(_sortDescending ? "↓" : "↑")}" : label;

    private void OnClearFiltersClicked(object sender, RoutedEventArgs args)
    {
        _controlsReady = false;
        TypeBox.SelectedIndex = 0;
        ExtensionBox.Text = string.Empty;
        MinimumSizeBox.Value = double.NaN;
        MaximumSizeBox.Value = double.NaN;
        MinimumUnitBox.SelectedIndex = 1;
        MaximumUnitBox.SelectedIndex = 1;
        ModifiedFromPicker.Date = null;
        ModifiedToPicker.Date = null;
        HiddenBox.IsChecked = false;
        SystemBox.IsChecked = false;
        ReadOnlyBox.IsChecked = false;
        (_sortField, _sortDescending) = FullSearchSortInteraction.RestoreRelevance();
        AdvancedFiltersPanel.Visibility = Visibility.Collapsed;
        AddFilterButton.Content = "Add filter";
        _controlsReady = true;
        UpdateSortPresentation();
        ApplySearch();
    }

    private void ApplySearch()
    {
        if (!_controlsReady || _closed || !_visible)
        {
            return;
        }

        _uiGeneration++;
        _searchCoordinator.Invalidate();
        _results.Clear();
        var query = QueryBox.Text.Trim();
        var filtersValid = TryGetCriteria(out var criteria, out var error);
        switch (FullSearchInputPolicy.Evaluate(query, _searchRuntime.HasSources(), filtersValid))
        {
            case FullSearchInputState.EmptyQuery:
                ValidationText.Text = string.Empty;
                StatusText.Text = "Enter a query to search.";
                return;
            case FullSearchInputState.NoSource:
                ValidationText.Text = string.Empty;
                StatusText.Text = "No active searchable index.";
                return;
            case FullSearchInputState.InvalidFilters:
                ValidationText.Text = error ?? "Invalid filters.";
                StatusText.Text = string.Empty;
                return;
        }

        ValidationText.Text = string.Empty;
        StatusText.Text = string.Empty;
        var request = _searchRuntime.CreateFullSearchRequest(query, FullSearchWindowLayout.ResultLimit, criteria!);
        _searchCoordinator.Request(request, _uiGeneration);
    }

    private bool TryGetCriteria(out FullSearchCriteria? criteria, out string? error) =>
        FullSearchCriteriaFactory.TryCreate(
            (FullSearchEntryType)Math.Max(TypeBox.SelectedIndex, 0),
            ExtensionBox.Text,
            MinimumSizeBox.Value,
            (FullSearchSizeUnit)Math.Max(MinimumUnitBox.SelectedIndex, 0),
            MaximumSizeBox.Value,
            (FullSearchSizeUnit)Math.Max(MaximumUnitBox.SelectedIndex, 0),
            ToDateOnly(ModifiedFromPicker.Date),
            ToDateOnly(ModifiedToPicker.Date),
            HiddenBox.IsChecked == true,
            SystemBox.IsChecked == true,
            ReadOnlyBox.IsChecked == true,
            _sortField,
            _sortDescending
                ? FullSearchSortDirection.Descending
                : FullSearchSortDirection.Ascending,
            out criteria,
            out error);

    private void OnSearchCompleted(SearchCompletion completion)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !_visible || !completion.IsCurrent || completion.UiGeneration != _uiGeneration)
            {
                return;
            }
            if (completion.Error is not null)
            {
                _results.Clear();
                StatusText.Text = "Search failed. A source may be temporarily unavailable.";
                AppLog.Write("Full Search failed.", completion.Error);
                return;
            }

            _results.Clear();
            foreach (var result in completion.Results ?? [])
            {
                var fields = _searchRuntime.GetFullSearchFields(result);
                if (fields is not null)
                {
                    _results.Add(FullSearchResultItem.Create(result, fields));
                }
            }
            if (_results.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
            }

            var notice = _searchRuntime.GetSourceStatusNotice();
            StatusText.Text = _results.Count switch
            {
                0 when notice is not null => $"No results. {notice}",
                0 => "No results.",
                FullSearchWindowLayout.ResultLimit when notice is not null => $"Showing up to {FullSearchWindowLayout.ResultLimit:N0} results. {notice}",
                FullSearchWindowLayout.ResultLimit => $"Showing up to {FullSearchWindowLayout.ResultLimit:N0} results.",
                _ when notice is not null => $"{_results.Count:N0} results. {notice}",
                _ => $"{_results.Count:N0} results."
            };
        });
    }

    private void OnSourcesChanged()
    {
        _searchCoordinator.Invalidate();
        DispatcherQueue.TryEnqueue(ApplySearch);
    }

    private void OnQueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Down && _results.Count > 0)
        {
            ResultsList.SelectedIndex = Math.Max(ResultsList.SelectedIndex, 0);
            ResultsList.Focus(FocusState.Keyboard);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Enter && SelectedResult is not null)
        {
            OpenSelected();
            args.Handled = true;
        }
    }

    private void OnResultsKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter && SelectedResult is not null)
        {
            OpenSelected();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.C && IsDown(VirtualKey.Control) && SelectedResult is not null)
        {
            CopySelectedPath();
            args.Handled = true;
        }
    }

    private void OnResultDoubleTapped(object sender, DoubleTappedRoutedEventArgs args)
    {
        if (SelectedResult is not null)
        {
            OpenSelected();
            args.Handled = true;
        }
    }

    private void OnResultRightTapped(object sender, RightTappedRoutedEventArgs args)
    {
        if (args.OriginalSource is FrameworkElement { DataContext: FullSearchResultItem item })
        {
            ResultsList.SelectedItem = item;
        }
    }

    private void OnContextMenuOpening(object sender, object args)
    {
        var selected = SelectedResult;
        OpenMenuItem.IsEnabled = selected is not null;
        RevealMenuItem.IsEnabled = selected is not null && _searchService.CanReveal(selected.Result.Action);
        CopyPathMenuItem.IsEnabled = selected is not null && _searchService.CanCopyText(selected.Result.Action);
    }

    private void OnOpenClicked(object sender, RoutedEventArgs args) => OpenSelected();

    private void OnRevealClicked(object sender, RoutedEventArgs args) => RevealSelected();

    private void OnCopyPathClicked(object sender, RoutedEventArgs args) => CopySelectedPath();

    private async void OpenSelected()
    {
        if (SelectedResult is not { } selected)
        {
            return;
        }

        try
        {
            await Task.Run(() => _searchService.Open(selected.Result.Action));
            StatusText.Text = "Opened selected result.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not open the selected result.";
            AppLog.Write("Full Search open failed.", exception);
        }
    }

    private async void RevealSelected()
    {
        if (SelectedResult is not { } selected)
        {
            return;
        }

        try
        {
            await Task.Run(() => _searchService.Reveal(selected.Result.Action));
            StatusText.Text = "Opened containing folder.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not reveal the selected result.";
            AppLog.Write("Full Search reveal failed.", exception);
        }
    }

    private void CopySelectedPath()
    {
        if (SelectedResult is not { } selected)
        {
            return;
        }

        try
        {
            var package = new DataPackage();
            package.SetText(_searchService.GetCopyText(selected.Result.Action));
            Clipboard.SetContent(package);
            StatusText.Text = "Path copied.";
        }
        catch (Exception exception)
        {
            StatusText.Text = "Could not copy the selected path.";
            AppLog.Write("Full Search copy path failed.", exception);
        }
    }

    private FullSearchResultItem? SelectedResult => ResultsList.SelectedItem as FullSearchResultItem;

    private void OnSettingsClicked(object sender, RoutedEventArgs args) => _showSettings();

    private void OnCollapseClicked(object sender, RoutedEventArgs args)
    {
        var query = QueryBox.Text;
        HideForCollapse();
        _collapse(query);
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _visible = false;
        _uiGeneration++;
        _searchRuntime.SourcesChanged -= OnSourcesChanged;
        _searchCoordinator.Completed -= OnSearchCompleted;
        _searchCoordinator.Dispose();
        AppWindow.Changed -= OnAppWindowChanged;
        ClosedByUser?.Invoke();
    }

    private static DateOnly? ToDateOnly(DateTimeOffset? value) =>
        value is null ? null : DateOnly.FromDateTime(value.Value.DateTime);

    private static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static bool IsSystemDark()
    {
        var color = new UISettings().GetColorValue(UIColorType.Background);
        return color.R + color.G + color.B < 384;
    }
}
