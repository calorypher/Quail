namespace Quail.App;

internal readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey, string DisplayText)
{
    internal const uint ModAlt = 0x0001;
    internal const uint ModControl = 0x0002;
    internal const uint ModShift = 0x0004;
    internal const uint ModWin = 0x0008;
    internal const uint SpaceVirtualKey = 0x20;
    private const uint AllowedModifiers = ModAlt | ModControl | ModShift | ModWin;

    public static bool TryParse(string? value, out HotkeyDefinition hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        uint modifiers = 0;
        foreach (var modifier in parts[..^1])
        {
            modifiers |= modifier.ToLowerInvariant() switch
            {
                "ctrl" or "control" => ModControl,
                "alt" => ModAlt,
                "shift" => ModShift,
                "win" or "windows" => ModWin,
                _ => uint.MaxValue
            };
        }

        if (modifiers == uint.MaxValue || !TryParseKey(parts[^1], out var key))
        {
            return false;
        }

        return TryCreate(modifiers, key, out hotkey);
    }

    internal static bool TryCreate(uint modifiers, uint virtualKey, out HotkeyDefinition hotkey)
    {
        hotkey = default;
        if (modifiers == 0 || (modifiers & ~AllowedModifiers) != 0 || !IsSupportedKey(virtualKey))
        {
            return false;
        }

        hotkey = new HotkeyDefinition(modifiers, virtualKey, FormatDisplay(modifiers, virtualKey));
        return true;
    }

    private static bool TryParseKey(string key, out uint virtualKey)
    {
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase))
        {
            virtualKey = SpaceVirtualKey;
            return true;
        }

        if (key.Length == 1 && char.IsLetterOrDigit(key[0]))
        {
            virtualKey = char.ToUpperInvariant(key[0]);
            return true;
        }

        virtualKey = 0;
        return false;
    }

    private static bool IsSupportedKey(uint virtualKey)
    {
        return virtualKey == SpaceVirtualKey ||
            virtualKey is >= 'A' and <= 'Z' ||
            virtualKey is >= '0' and <= '9';
    }

    private static string FormatDisplay(uint modifiers, uint key)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        if ((modifiers & ModWin) != 0) parts.Add("Win");
        parts.Add(key == SpaceVirtualKey ? "Space" : ((char)key).ToString());
        return string.Join('+', parts);
    }
}
