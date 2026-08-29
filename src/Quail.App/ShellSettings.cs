namespace Quail.App;

internal sealed record ShellSettings(string Hotkey, string Theme)
{
    public static ShellSettings Default { get; } = new("Ctrl+Alt+Space", "System");

    public ShellSettings Normalize() => new(
        string.IsNullOrWhiteSpace(Hotkey) ? Default.Hotkey : Hotkey.Trim(),
        Theme is "System" or "Light" or "Dark" ? Theme : Default.Theme);
}
