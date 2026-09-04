namespace Quail.FileSystem;

public static class UsnReason
{
    public const uint DataOverwrite = 0x00000001;
    public const uint DataExtend = 0x00000002;
    public const uint DataTruncation = 0x00000004;
    public const uint NamedDataOverwrite = 0x00000010;
    public const uint NamedDataExtend = 0x00000020;
    public const uint NamedDataTruncation = 0x00000040;
    public const uint FileCreate = 0x00000100;
    public const uint FileDelete = 0x00000200;
    public const uint RenameOldName = 0x00001000;
    public const uint RenameNewName = 0x00002000;
    public const uint BasicInfoChange = 0x00008000;
    public const uint ReparsePointChange = 0x00100000;
    public const uint StreamChange = 0x00200000;
    public const uint TransactedChange = 0x00400000;

    public const uint MetadataRefreshMask =
        DataOverwrite | DataExtend | DataTruncation |
        NamedDataOverwrite | NamedDataExtend | NamedDataTruncation |
        FileCreate | BasicInfoChange | ReparsePointChange | StreamChange | TransactedChange;

    public static bool RequiresMetadataRefresh(uint reason) => (reason & MetadataRefreshMask) != 0;
    public static bool IsFileCreate(uint reason) => (reason & FileCreate) != 0;
    public static bool IsFileDelete(uint reason) => (reason & FileDelete) != 0;
    public static bool IsRenameOldName(uint reason) => (reason & RenameOldName) != 0;
    public static bool IsRenameNewName(uint reason) => (reason & RenameNewName) != 0;
}
