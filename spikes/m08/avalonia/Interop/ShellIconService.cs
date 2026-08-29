using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using Avalonia.Controls;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace Quail.M08.Avalonia.Interop;

internal sealed class ShellIconService : IDisposable
{
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint ShgfiIcon = 0x00000100;
    private const uint ShgfiSmallIcon = 0x00000001;
    private const uint ShgfiUseFileAttributes = 0x00000010;

    private readonly ConcurrentDictionary<string, AvaloniaBitmap> _icons = new(StringComparer.OrdinalIgnoreCase);

    public AvaloniaBitmap Get(MockResult result)
    {
        var cacheKey = result.Kind == "directory" ? "directory" : result.Extension;
        return _icons.GetOrAdd(cacheKey, _ => LoadIcon(result));
    }

    public bool TryGet(MockResult result, out AvaloniaBitmap? icon)
    {
        var cacheKey = result.Kind == "directory" ? "directory" : result.Extension;
        return _icons.TryGetValue(cacheKey, out icon);
    }

    public Task LoadAsync(MockResult result) => Task.Run(() => Get(result));

    public WindowIcon GetTrayIcon()
    {
        var genericExecutable = new MockResult("file", "Quail M08", "Quail.M08.Avalonia.exe", ".exe", null, DateTimeOffset.UtcNow);
        return new WindowIcon(LoadIcon(genericExecutable));
    }

    private static AvaloniaBitmap LoadIcon(MockResult result)
    {
        var attributes = result.Kind == "directory" ? FileAttributeDirectory : FileAttributeNormal;
        var samplePath = result.Kind == "directory" ? "Folder" : $"item{result.Extension}";
        var flags = ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes;
        _ = NativeMethods.SHGetFileInfo(
            samplePath,
            attributes,
            out var fileInfo,
            (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
            flags);

        if (fileInfo.HIcon == nint.Zero)
        {
            throw new InvalidOperationException($"Windows Shell did not provide an icon for {samplePath}.");
        }

        try
        {
            using var icon = Icon.FromHandle(fileInfo.HIcon);
            using var image = icon.ToBitmap();
            using var stream = new MemoryStream();
            image.Save(stream, ImageFormat.Png);
            stream.Position = 0;
            return new AvaloniaBitmap(stream);
        }
        finally
        {
            NativeMethods.DestroyIcon(fileInfo.HIcon);
        }
    }

    public void Dispose()
    {
        foreach (var icon in _icons.Values)
        {
            icon.Dispose();
        }

        _icons.Clear();
    }
}
