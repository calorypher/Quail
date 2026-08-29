using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Quail.App;

internal sealed class SettingsSurface : Grid
{
    private readonly TextBox _hotkeyBox;
    private readonly ComboBox _themeBox;
    private readonly TextBlock _errorText;
    private readonly Func<ShellSettings, Task<string?>> _save;
    private readonly Action _beginHotkeyCapture;
    private readonly Func<bool> _restoreHotkey;

    public SettingsSurface(
        ShellSettings settings,
        Func<ShellSettings, Task<string?>> save,
        Action beginHotkeyCapture,
        Func<bool> restoreHotkey)
    {
        _save = save;
        _beginHotkeyCapture = beginHotkeyCapture;
        _restoreHotkey = restoreHotkey;
        Padding = new Thickness(24);
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Quail settings",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14)
        };
        Children.Add(title);

        _hotkeyBox = new TextBox
        {
            Text = settings.Hotkey,
            Header = "Quick Search hotkey",
            PlaceholderText = "Press a shortcut",
            IsReadOnly = true
        };
        _hotkeyBox.KeyDown += OnHotkeyKeyDown;
        _hotkeyBox.LostFocus += (_, _) => TryRestoreHotkey();

        _themeBox = new ComboBox
        {
            Header = "Theme",
            ItemsSource = new[] { "System", "Light", "Dark" },
            SelectedItem = settings.Theme,
            MinWidth = 180
        };
        _errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

        var useDefault = new Button
        {
            Content = "Use default",
            VerticalAlignment = VerticalAlignment.Bottom
        };
        useDefault.Click += (_, _) =>
        {
            SetHotkeyText(ShellSettings.Default.Hotkey);
        };

        var hotkeyRow = new Grid { ColumnSpacing = 12 };
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hotkeyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hotkeyRow.Children.Add(_hotkeyBox);
        Grid.SetColumn(useDefault, 1);
        hotkeyRow.Children.Add(useDefault);

        var quickSearch = new StackPanel { Spacing = 6 };
        quickSearch.Children.Add(new TextBlock { Text = "Quick Search", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        quickSearch.Children.Add(Description("Choose the keyboard shortcut used to summon Quick Search."));
        quickSearch.Children.Add(hotkeyRow);

        var appearance = new Grid { ColumnSpacing = 12 };
        appearance.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        appearance.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel { Spacing = 3 };
        copy.Children.Add(new TextBlock { Text = "Appearance", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        copy.Children.Add(Description("Choose how Quail follows the system theme."));
        appearance.Children.Add(copy);
        Grid.SetColumn(_themeBox, 1);
        appearance.Children.Add(_themeBox);

        var indexes = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Style = Application.Current.Resources["QuailSettingsNavigationButtonStyle"] as Style
        };
        var indexContent = new Grid { ColumnSpacing = 10 };
        indexContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        indexContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var indexCopy = new StackPanel { Spacing = 1 };
        indexCopy.Children.Add(new TextBlock { Text = "Indexes", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        indexCopy.Children.Add(Description("Manage local volumes for Quick Search."));
        indexContent.Children.Add(indexCopy);
        var chevron = new FontIcon { Glyph = "", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(chevron, 1);
        indexContent.Children.Add(chevron);
        indexes.Content = indexContent;
        indexes.Click += (_, _) =>
        {
            if (TryRestoreHotkey())
            {
                ManageIndexesRequested = true;
                Close();
            }
        };

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Card(quickSearch));
        content.Children.Add(Card(appearance));
        content.Children.Add(indexes);
        content.Children.Add(CreateErrorCard(_errorText));
        Grid.SetRow(content, 1);
        Children.Add(content);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var cancel = new Button { Content = "Cancel" };
        cancel.Click += (_, _) =>
        {
            if (TryRestoreHotkey())
            {
                Close();
            }
        };
        var saveButton = new Button
        {
            Content = "Save",
            Style = Application.Current.Resources["QuailPrimaryActionButtonStyle"] as Style
        };
        saveButton.Click += async (_, _) =>
        {
            await SaveAsync();
        };
        footer.Children.Add(cancel);
        footer.Children.Add(saveButton);
        Grid.SetRow(footer, 2);
        Children.Add(footer);
        KeyDown += OnKeyDown;
    }

    public bool ManageIndexesRequested { get; private set; }
    public event Action? Closed;

    private async Task SaveAsync()
    {
        var error = await _save(new ShellSettings(_hotkeyBox.Text, (string?)_themeBox.SelectedItem ?? "System"));
        if (error is null)
        {
            Close();
            return;
        }
        _errorText.Text = error;
        _errorText.Visibility = Visibility.Visible;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Escape && TryRestoreHotkey())
        {
            args.Handled = true;
            Close();
        }
    }

    private void OnHotkeyKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is VirtualKey.Enter or VirtualKey.Escape) return;
        args.Handled = true;
        _beginHotkeyCapture();
        if (HotkeyCapture.TryCapture((uint)args.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu), IsDown(VirtualKey.Shift), IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows), out var displayText))
        {
            SetHotkeyText(displayText);
            _errorText.Visibility = Visibility.Collapsed;
        }
    }

    private bool TryRestoreHotkey()
    {
        if (SettingsHotkeyRestoreGuard.TryRestore(_restoreHotkey, out var error)) return true;
        _errorText.Text = error;
        _errorText.Visibility = Visibility.Visible;
        return false;
    }

    private void Close()
    {
        Closed?.Invoke();
    }

    private void SetHotkeyText(string value)
    {
        _hotkeyBox.Text = value;
        _hotkeyBox.SelectAll();
    }

    private static bool IsDown(VirtualKey key) => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private static Border Card(UIElement content)
    {
        return new Border
        {
            Style = Application.Current.Resources["QuailSettingsCardStyle"] as Style,
            Child = content
        };
    }

    private static Border CreateErrorCard(TextBlock error)
    {
        var panel = new StackPanel { Spacing = 6 };
        panel.Children.Add(new TextBlock { Text = "Could not save settings", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(error);
        var card = Card(panel);
        card.Visibility = Visibility.Collapsed;
        error.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => card.Visibility = error.Visibility);
        return card;
    }

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
