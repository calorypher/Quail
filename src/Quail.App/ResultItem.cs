using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Quail.Core;

namespace Quail.App;

public sealed class ResultItem : INotifyPropertyChanged
{
    public required SearchResultAction Action { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Kind { get; init; }
    public required string Metadata { get; init; }
    public string IconGlyph => Kind == "Folder" ? "\uE8B7" : "\uE8A5";

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            Notify();
            Notify(nameof(FallbackIconVisibility));
        }
    }

    public Visibility FallbackIconVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;

    private bool _isKeyboardSelected;
    public bool IsKeyboardSelected
    {
        get => _isKeyboardSelected;
        set
        {
            if (_isKeyboardSelected == value)
            {
                return;
            }

            _isKeyboardSelected = value;
            Notify();
            Notify(nameof(KeyboardSelectionVisibility));
        }
    }

    public Visibility KeyboardSelectionVisibility => IsKeyboardSelected ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Notify([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
