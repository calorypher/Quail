using Quail.Core;

namespace Quail.App;

internal static class FileSearchPresentation
{
    public static ResultItem Map(SearchResult result) => new()
    {
        Action = result.Action,
        Name = result.Name,
        Path = result.FullPath ?? "Path unavailable",
        Kind = result.IsDirectory ? "Folder" : "File",
        Metadata = FileSearchMetadata.Format(result),
    };
}
