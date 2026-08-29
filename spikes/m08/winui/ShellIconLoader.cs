using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Quail.M08.WinUi;

internal sealed class ShellIconLoader
{
    private const int IconSize = 32;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource?> LoadAsync(string kind, string extension)
    {
        var diagnosticsPath = M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs());
        var key = kind == "directory" ? "directory" : extension;
        M08Diagnostics.WriteMessage(diagnosticsPath, $"ShellIconLoader: loading key '{key}'.");
        if (_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var loaded = await LoadShellIconAsync(kind, extension);
        M08Diagnostics.WriteMessage(diagnosticsPath, $"ShellIconLoader: native icon loaded for '{key}'.");
        return _cache.GetOrAdd(key, loaded);
    }

    private static async Task<ImageSource?> LoadShellIconAsync(string kind, string extension)
    {
        var fileInfo = new NativeMethods.ShFileInfo();
        var attributes = kind == "directory" ? NativeMethods.FileAttributeDirectory : 0u;
        var path = kind == "directory" ? "folder" : $"placeholder{extension}";
        var result = NativeMethods.SHGetFileInfo(
            path,
            attributes,
            ref fileInfo,
            (uint)Marshal.SizeOf<NativeMethods.ShFileInfo>(),
            NativeMethods.ShgfiIcon | NativeMethods.ShgfiSmallIcon | NativeMethods.ShgfiUseFileAttributes);

        if (result == 0 || fileInfo.Icon == 0)
        {
            return null;
        }

        try
        {
            M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: converting HICON to ImageSource.");
            return await CreateImageSourceAsync(fileInfo.Icon);
        }
        finally
        {
            NativeMethods.DestroyIcon(fileInfo.Icon);
        }
    }

    private static async Task<ImageSource?> CreateImageSourceAsync(nint icon)
    {
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: creating managed icon wrapper.");
        using var borrowedIcon = Icon.FromHandle(icon);
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: managed icon wrapper created.");
        using var iconCopy = (Icon)borrowedIcon.Clone();
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: icon cloned.");
        using var bitmap = iconCopy.ToBitmap();
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: bitmap created.");
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: PNG encoded.");
        output.Position = 0;
        using var input = output.AsRandomAccessStream();
        var source = new BitmapImage();
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: BitmapImage created.");
        await source.SetSourceAsync(input);
        M08Diagnostics.WriteMessage(M08Diagnostics.PathFromArguments(Environment.GetCommandLineArgs()), "ShellIconLoader: BitmapImage populated.");
        return source;
    }
}
