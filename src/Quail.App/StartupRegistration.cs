using Microsoft.Win32;

namespace Quail.App;

internal interface IStartupRegistration
{
    bool IsEnabled { get; }
    string? SetEnabled(bool enabled);
}

internal sealed class StartupRegistration : IStartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Quail";
    private readonly Func<string?> _executablePath;
    private readonly Func<RegistryKey?> _openRunKey;

    public StartupRegistration()
        : this(() => Environment.ProcessPath, () => Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
    {
    }

    internal StartupRegistration(Func<string?> executablePath, Func<RegistryKey?> openRunKey)
    {
        _executablePath = executablePath;
        _openRunKey = openRunKey;
    }

    public bool IsEnabled
    {
        get
        {
            var expected = InstalledCommand;
            try
            {
                using var key = _openRunKey();
                return string.Equals(key?.GetValue(ValueName) as string, expected, StringComparison.Ordinal);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                AppLog.Write("Could not read Quail startup registration.", exception);
                return false;
            }
        }
    }

    public string? SetEnabled(bool enabled)
    {
        var expected = InstalledCommand;
        if (enabled && GetExpectedCommand(_executablePath()) is null)
        {
            return "Launch on startup is available after Quail is installed.";
        }

        try
        {
            using var key = _openRunKey();
            if (key is null) return "Could not open your Windows startup settings.";
            if (enabled)
            {
                key.SetValue(ValueName, expected, RegistryValueKind.String);
            }
            else if (string.Equals(key.GetValue(ValueName) as string, expected, StringComparison.Ordinal))
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return IsEnabled == enabled ? null : "Windows did not apply the startup setting.";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Write("Could not update Quail startup registration.", exception);
            return "Could not update your Windows startup setting.";
        }
    }

    internal static string? GetExpectedCommand(string? executablePath = null)
    {
        executablePath ??= Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) return null;
        var expectedPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Quail", "Quail.exe");
        return string.Equals(Path.GetFullPath(executablePath), expectedPath, StringComparison.OrdinalIgnoreCase)
            ? InstalledCommand
            : null;
    }

    private static string InstalledCommand => $"\"{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Quail", "Quail.exe")}\"";
}
