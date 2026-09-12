namespace Quail.App;

internal readonly record struct CaptionButtonTheme(byte Red, byte Green, byte Blue);

internal static class CaptionButtonThemePolicy
{
    public static CaptionButtonTheme ForEffectiveTheme(bool useDark) => useDark
        ? new CaptionButtonTheme(0xF4, 0xF8, 0xFC)
        : new CaptionButtonTheme(0x13, 0x22, 0x35);
}
