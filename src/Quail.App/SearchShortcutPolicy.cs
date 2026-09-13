namespace Quail.App;

internal enum SearchShortcutAction
{
    None,
    Open,
    Reveal,
    CopyPath,
    SwitchMode
}

internal static class SearchShortcutPolicy
{
    public static SearchShortcutAction Resolve(
        bool isEnter,
        bool isC,
        bool controlDown,
        bool altDown,
        bool shiftDown,
        bool hasSelectedResult,
        bool canReveal,
        bool canCopyPath)
    {
        if (isEnter && altDown)
        {
            return SearchShortcutAction.SwitchMode;
        }

        if (isEnter && controlDown && hasSelectedResult && canReveal)
        {
            return SearchShortcutAction.Reveal;
        }

        if (isC && controlDown && shiftDown && hasSelectedResult && canCopyPath)
        {
            return SearchShortcutAction.CopyPath;
        }

        return isEnter && hasSelectedResult && !controlDown
            ? SearchShortcutAction.Open
            : SearchShortcutAction.None;
    }

    public static bool PreservesQueryTextCopy(bool queryBoxFocused, bool controlDown, bool shiftDown) =>
        queryBoxFocused && controlDown && !shiftDown;
}
