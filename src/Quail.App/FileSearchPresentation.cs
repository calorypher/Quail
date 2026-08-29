using Quail.Core;

namespace Quail.App;

internal static class FileSearchPresentation
{
    public static ResultItem Map(IndexedFileSearchResult result) => new()
    {
        SourceIdentity = result.SourceIdentity,
        FileId = result.Result.FileId,
        Name = result.Result.Name,
        Path = result.Result.FullPath ?? "Path unavailable",
        Kind = result.Result.IsDirectory ? "Folder" : "File",
        Metadata = FileSearchMetadata.Format(result.Result),
    };
}
