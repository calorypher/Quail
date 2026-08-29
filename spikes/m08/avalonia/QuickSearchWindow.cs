using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Quail.M08.Avalonia.Interop;

namespace Quail.M08.Avalonia;

internal sealed class QuickSearchWindow : Window
{
    private const double OverlayWidth = 680;
    private const double OverlayHeight = 360;
    private const double ResultRowHeight = 44;

    private readonly IReadOnlyList<MockResult> _allResults;
    private readonly PipeEventWriter _events;
    private readonly ShellIconService _icons;
    private readonly Action _visibleReady;
    private readonly bool _loadShellIcons;
    private readonly TextBox _queryBox;
    private readonly StackPanel _resultPanel;
    private readonly ScrollViewer _resultScroll;
    private readonly List<RowPresentation> _rows = [];
    private IReadOnlyList<MockResult> _filteredResults = [];
    private int _selectedIndex = -1;
    private bool _summoning;
    private bool _shellIconsRequested;
    private bool _closing;
    private Palette _palette;

    public QuickSearchWindow(
        IReadOnlyList<MockResult> allResults,
        PipeEventWriter events,
        ShellIconService icons,
        Action visibleReady,
        bool loadShellIcons)
    {
        _allResults = allResults;
        _events = events;
        _icons = icons;
        _visibleReady = visibleReady;
        _loadShellIcons = loadShellIcons;
        _palette = Palette.Light;

        Title = "Quail M08 Avalonia spike";
        Width = OverlayWidth;
        Height = OverlayHeight;
        MinWidth = OverlayWidth;
        MaxWidth = OverlayWidth;
        MinHeight = OverlayHeight;
        MaxHeight = OverlayHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowDecorations = WindowDecorations.None;
        FontFamily = new FontFamily("Segoe UI Variable, Segoe UI");
        Background = Brushes.Transparent;

        _queryBox = new TextBox
        {
            PlaceholderText = "Search files",
            FontSize = 17,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 10),
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent
        };
        _queryBox.TextChanged += QueryBox_OnTextChanged;
        _queryBox.KeyDown += QueryBox_OnKeyDown;

        _resultPanel = new StackPanel { Spacing = 2 };
        _resultScroll = new ScrollViewer
        {
            Content = _resultPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(10, 0)
        };
        Content = BuildContent();
        ActualThemeVariantChanged += (_, _) => ApplyPalette();
        Deactivated += (_, _) =>
        {
            if (!_closing && !_summoning && IsVisible)
            {
                HideOverlay();
            }
        };

        ApplyPalette();
        ApplyFilter();
    }

    public void ShowOverlay()
    {
        _summoning = true;
        _queryBox.Text = string.Empty;
        PlaceAtCursorMonitor();
        if (!IsVisible)
        {
            Show();
        }

        var hwnd = GetWindowHandle();
        if (hwnd != nint.Zero)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SwShow);
        }

        Activate();

        if (hwnd != nint.Zero)
        {
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }

        _queryBox.Focus();
        Dispatcher.UIThread.Post(SignalVisibleReady, DispatcherPriority.Render);
    }

    public void HideOverlay()
    {
        if (!IsVisible)
        {
            return;
        }

        Hide();
        _events.Hidden();
    }

    public void PrepareForExit() => _closing = true;

    private Control BuildContent()
    {
        var queryBorder = new Border
        {
            CornerRadius = new CornerRadius(9),
            Margin = new Thickness(10, 10, 10, 6),
            Child = _queryBox
        };


        var footer = new TextBlock
        {
            Text = "↑↓ Navigate     Enter Select     Esc Dismiss",
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0)
        };

        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,28")
        };
        grid.Children.Add(queryBorder);
        Grid.SetRow(_resultScroll, 1);
        grid.Children.Add(_resultScroll);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        return new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Child = grid,
            Tag = new ChromeParts(queryBorder, footer)
        };
    }

    private void SignalVisibleReady()
    {
        _queryBox.Focus();
        NativeMethods.DwmFlush();
        var hwnd = GetWindowHandle();
        NativeMethods.GetWindowRect(hwnd, out var rect);
        var monitor = NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = rect.Left, Y = rect.Top }, NativeMethods.MonitorDefaultToNearest);
        var dpi = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.EffectiveDpi, out var dpiX, out _) == 0 ? dpiX : 96;
        _summoning = false;
        _events.VisibleReady(hwnd, NativeMethods.GetFocus(), ReferenceEquals(FocusManager?.GetFocusedElement(), _queryBox), dpi, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        _visibleReady();
        if (_loadShellIcons)
        {
            RequestShellIcons();
        }
    }

    private async void RequestShellIcons()
    {
        if (_shellIconsRequested)
        {
            return;
        }

        _shellIconsRequested = true;
        try
        {
            foreach (var result in _allResults)
            {
                await _icons.LoadAsync(result);
            }

            ApplyFilter();
            _events.ShellIconsReady();
        }
        catch (Exception)
        {
            // A failed decorative icon conversion must not affect the overlay.
        }
    }

    private void PlaceAtCursorMonitor()
    {
        if (!NativeMethods.GetCursorPos(out var cursor))
        {
            return;
        }

        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MONITORINFO { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        if (monitor == nint.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var scale = 1.0;
        if (NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MonitorDpiType.EffectiveDpi, out var dpi, out _) == 0)
        {
            scale = dpi / 96.0;
        }

        var width = (int)Math.Round(OverlayWidth * scale);
        var height = (int)Math.Round(OverlayHeight * scale);
        Position = new PixelPoint(
            info.Work.Left + Math.Max(0, (info.Work.Right - info.Work.Left - width) / 2),
            info.Work.Top + Math.Max(0, (info.Work.Bottom - info.Work.Top - height) / 2));
    }

    private nint GetWindowHandle() => TryGetPlatformHandle()?.Handle ?? nint.Zero;

    private void QueryBox_OnTextChanged(object? sender, TextChangedEventArgs eventArgs)
    {
        ApplyFilter();
        _events.QueryChanged(_queryBox.Text ?? string.Empty, _filteredResults.Count);
    }

    private void QueryBox_OnKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        switch (eventArgs.Key)
        {
            case Key.Down:
                MoveSelection(1);
                eventArgs.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                eventArgs.Handled = true;
                break;
            case Key.Escape:
                HideOverlay();
                eventArgs.Handled = true;
                break;
            case Key.Enter when _selectedIndex >= 0 && _selectedIndex < _filteredResults.Count:
                _events.Confirmed(_filteredResults[_selectedIndex].Name);
                eventArgs.Handled = true;
                break;
        }
    }

    private void ApplyFilter()
    {
        var query = _queryBox.Text?.Trim() ?? string.Empty;
        _filteredResults = _allResults
            .Where(result => string.IsNullOrEmpty(query)
                || result.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || result.Path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        _selectedIndex = _filteredResults.Count == 0 ? -1 : 0;
        RebuildRows();

        if (_selectedIndex >= 0)
        {
            EmitSelectionChanged();
        }
    }

    private void MoveSelection(int change)
    {
        if (_filteredResults.Count == 0)
        {
            return;
        }

        _selectedIndex = Math.Clamp(_selectedIndex + change, 0, _filteredResults.Count - 1);
        RefreshRowStates();
        _rows[_selectedIndex].Border.BringIntoView();
        _events.SelectionScrollRequested(_selectedIndex);
        EmitSelectionChanged();
    }

    private void RebuildRows()
    {
        _resultPanel.Children.Clear();
        _rows.Clear();
        for (var index = 0; index < _filteredResults.Count; index++)
        {
            var currentIndex = index;
            var result = _filteredResults[index];
            var row = CreateResultRow(result);
            row.Border.PointerPressed += (_, _) =>
            {
                _selectedIndex = currentIndex;
                RefreshRowStates();
                EmitSelectionChanged();
                _queryBox.Focus();
            };
            row.Border.PointerEntered += (_, _) =>
            {
                row.IsHovered = true;
                ApplyRowState(row, currentIndex == _selectedIndex);
            };
            row.Border.PointerExited += (_, _) =>
            {
                row.IsHovered = false;
                ApplyRowState(row, currentIndex == _selectedIndex);
            };
            _rows.Add(row);
            _resultPanel.Children.Add(row.Border);
        }

        RefreshRowStates();
    }

    private RowPresentation CreateResultRow(MockResult result)
    {
        var icon = new Image
        {
            Source = _icons.TryGet(result, out var shellIcon) ? shellIcon : null,
            Width = 20,
            Height = 20,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 6, 0)
        };
        var primary = new TextBlock
        {
            Text = result.Name,
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var secondary = new TextBlock
        {
            Text = result.Path,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var text = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(primary);
        text.Children.Add(secondary);
        var metadata = new TextBlock
        {
            Text = FormatMetadata(result),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(4, 0, 8, 0)
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*,106") };
        grid.Children.Add(icon);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        Grid.SetColumn(metadata, 2);
        grid.Children.Add(metadata);

        var border = new Border
        {
            Height = ResultRowHeight,
            CornerRadius = new CornerRadius(5),
            Child = grid
        };
        return new RowPresentation(border, primary, secondary, metadata);
    }

    private static string FormatMetadata(MockResult result) => result.Kind == "directory"
        ? "Folder"
        : $"{result.Extension.TrimStart('.').ToUpperInvariant()}  {FormatSize(result.SizeBytes)}";

    private static string FormatSize(long? sizeBytes) => sizeBytes switch
    {
        null => string.Empty,
        < 1024 => $"{sizeBytes} B",
        < 1024 * 1024 => $"{sizeBytes / 1024d:F0} KB",
        _ => $"{sizeBytes / (1024d * 1024d):F1} MB"
    };

    private void RefreshRowStates()
    {
        for (var index = 0; index < _rows.Count; index++)
        {
            ApplyRowState(_rows[index], index == _selectedIndex);
        }
    }

    private void ApplyRowState(RowPresentation row, bool selected)
    {
        row.Border.Background = selected
            ? _palette.Selected
            : row.IsHovered ? _palette.Hover : Brushes.Transparent;
        row.Primary.Foreground = selected ? _palette.SelectedText : _palette.PrimaryText;
        row.Secondary.Foreground = selected ? _palette.SelectedText : _palette.SecondaryText;
        row.Metadata.Foreground = selected ? _palette.SelectedText : _palette.SecondaryText;
    }

    private void EmitSelectionChanged()
    {
        if (_selectedIndex >= 0 && _selectedIndex < _filteredResults.Count)
        {
            _events.SelectionChanged(_selectedIndex, _filteredResults[_selectedIndex].Name);
        }
    }

    private void ApplyPalette()
    {
        _palette = ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark ? Palette.Dark : Palette.Light;
        if (Content is not Border chrome || chrome.Child is not Grid grid || chrome.Tag is not ChromeParts parts)
        {
            return;
        }

        chrome.Background = _palette.Surface;
        chrome.BorderBrush = _palette.Border;
        parts.QueryBorder.Background = _palette.QuerySurface;
        _queryBox.Foreground = _palette.PrimaryText;
        _queryBox.CaretBrush = _palette.PrimaryText;
        parts.Footer.Foreground = _palette.SecondaryText;
        RefreshRowStates();
    }

    private sealed class RowPresentation(Border border, TextBlock primary, TextBlock secondary, TextBlock metadata)
    {
        public Border Border { get; } = border;
        public TextBlock Primary { get; } = primary;
        public TextBlock Secondary { get; } = secondary;
        public TextBlock Metadata { get; } = metadata;
        public bool IsHovered { get; set; }
    }

    private sealed record ChromeParts(Border QueryBorder, TextBlock Footer);

    private sealed record Palette(
        IBrush Surface,
        IBrush QuerySurface,
        IBrush Border,
        IBrush PrimaryText,
        IBrush SecondaryText,
        IBrush Selected,
        IBrush SelectedText,
        IBrush Hover)
    {
        public static Palette Light { get; } = new(
            Brush.Parse("#FFF9F9F9"),
            Brush.Parse("#FFFFFFFF"),
            Brush.Parse("#1F000000"),
            Brush.Parse("#FF202020"),
            Brush.Parse("#FF666666"),
            Brush.Parse("#FF1976D2"),
            Brushes.White,
            Brush.Parse("#14000000"));

        public static Palette Dark { get; } = new(
            Brush.Parse("#FF202020"),
            Brush.Parse("#FF2C2C2C"),
            Brush.Parse("#33FFFFFF"),
            Brush.Parse("#FFF5F5F5"),
            Brush.Parse("#FFB6B6B6"),
            Brush.Parse("#FF2B88D8"),
            Brushes.White,
            Brush.Parse("#22FFFFFF"));
    }
}
