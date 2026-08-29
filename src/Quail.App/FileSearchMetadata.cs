using Quail.Core;
using System.Globalization;

namespace Quail.App;

internal static class FileSearchMetadata
{
    public static string Format(FileSearchResult result)
    {
        if (result.IsDirectory) return "Folder";

        var type = string.IsNullOrWhiteSpace(result.Extension)
            ? "File"
            : result.Extension.ToUpperInvariant();
        return result.LogicalSize is long size
            ? $"{type} · {FormatSize(size)}"
            : type;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{(bytes / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{(bytes / (1024d * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} MB";
        return $"{(bytes / (1024d * 1024 * 1024)).ToString("0.#", CultureInfo.InvariantCulture)} GB";
    }
}
