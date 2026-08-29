namespace Quail.App;

internal static class HotkeyCapture
{
    internal static bool TryCapture(uint virtualKey, bool controlDown, bool altDown, bool shiftDown, bool winDown, out string displayText)
    {
        if (virtualKey is >= 'a' and <= 'z')
        {
            virtualKey = (uint)char.ToUpperInvariant((char)virtualKey);
        }

        var modifiers = 0u;
        if (controlDown) modifiers |= HotkeyDefinition.ModControl;
        if (altDown) modifiers |= HotkeyDefinition.ModAlt;
        if (shiftDown) modifiers |= HotkeyDefinition.ModShift;
        if (winDown) modifiers |= HotkeyDefinition.ModWin;

        if (!HotkeyDefinition.TryCreate(modifiers, virtualKey, out var hotkey))
        {
            displayText = string.Empty;
            return false;
        }

        displayText = hotkey.DisplayText;
        return true;
    }
}
