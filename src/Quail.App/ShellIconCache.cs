using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Quail.App;

internal sealed class ShellIconCache : IDisposable
{
    private const uint FileAttributeDirectory = 0x10;
    private const uint FileAttributeNormal = 0x80;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private readonly object _gate = new();
    private readonly BoundedLruCache<string, byte[]> _cache = new(128);
    private bool _disposed;

    public async Task<ImageSource?> LoadAsync(string path, bool isDirectory)
    {
        var key = isDirectory ? "folder" : Path.GetExtension(path).ToUpperInvariant();
        if (string.IsNullOrEmpty(key)) key = "file";
        byte[]? bytes;
        lock (_gate)
        {
            if (_disposed) return null;
            _cache.TryGetValue(key, out bytes);
        }

        if (bytes is null)
        {
            bytes = await Task.Run(() => LoadShellIconBytes(path, isDirectory));
            if (bytes is null) return null;
            lock (_gate)
            {
                if (_disposed) return null;
                _cache.Set(key, bytes);
            }
        }

        return await CreateImageAsync(bytes);
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
    }

    private static byte[]? LoadShellIconBytes(string path, bool isDirectory)
    {
        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            path,
            isDirectory ? FileAttributeDirectory : FileAttributeNormal,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            ShgfiIcon | ShgfiSmallIcon | ShgfiUseFileAttributes);
        if (result == 0 || info.Icon == 0) return null;

        try
        {
            using var icon = Icon.FromHandle(info.Icon);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return stream.ToArray();
        }
        finally
        {
            DestroyIcon(info.Icon);
        }
    }

    private static async Task<ImageSource> CreateImageAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer());
        stream.Seek(0);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream);
        stream.Dispose();
        return image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint icon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
}
