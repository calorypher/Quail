using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;

namespace Quail.M08.WinUi.Models;

public sealed class MockResult : INotifyPropertyChanged
{
    private ImageSource? _iconSource;

    public required string Kind { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Extension { get; init; }
    public long? SizeBytes { get; init; }
    public DateTime ModifiedUtc { get; init; }

    public string Metadata => SizeBytes is null
        ? "Folder"
        : $"{FormatSize(SizeBytes.Value)} · {ModifiedUtc.ToLocalTime():MMM d}";

    public string IconGlyph => Kind == "directory" ? "\uE8B7" : "\uE8A5";

    public ImageSource? IconSource
    {
        get => _iconSource;
        set
        {
            if (ReferenceEquals(_iconSource, value))
            {
                return;
            }

            _iconSource = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024d:F1} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024d * 1024d):F1} MB",
            _ => $"{bytes / (1024d * 1024d * 1024d):F1} GB"
        };
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
