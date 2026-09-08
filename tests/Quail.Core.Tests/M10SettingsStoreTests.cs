using Quail.App;
using Microsoft.Win32;

namespace Quail.Core.Tests;

public sealed class M10SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"quail-m10-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_UsesDefaultsWhenConfigDoesNotExist()
    {
        var settings = await CreateStore().LoadAsync();

        Assert.Equal(ShellSettings.Default, settings);
        Assert.Equal("Alt+Space", settings.Hotkey);
    }

    [Fact]
    public async Task SaveAsync_ReloadsNormalizedSettingsAndReplacesPreviousFile()
    {
        var store = CreateStore();

        await store.SaveAsync(new ShellSettings("ctrl+alt+k", "Dark"));
        await store.SaveAsync(new ShellSettings("Ctrl+Alt+Space", "Unexpected"));

        var settings = await store.LoadAsync();

        Assert.Equal("Ctrl+Alt+Space", settings.Hotkey);
        Assert.Equal("System", settings.Theme);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task LoadAsync_FallsBackForMalformedJson()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{ not json }");

        var settings = await CreateStore().LoadAsync();

        Assert.Equal(ShellSettings.Default, settings);
    }

    [Fact]
    public async Task LoadAsync_FallsBackForInvalidStoredHotkey()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{\"Hotkey\":\"Ctrl+Alt+F13\",\"Theme\":\"Light\"}");

        var settings = await CreateStore().LoadAsync();

        Assert.Equal(ShellSettings.Default, settings);
        Assert.Equal("Alt+Space", settings.Hotkey);
    }

    [Fact]
    public async Task LoadAsync_PreservesExistingValidPreviousDefaultWithoutMigration()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(SettingsPath, "{\"Hotkey\":\"Ctrl+Alt+Space\",\"Theme\":\"Light\"}");

        var settings = await CreateStore().LoadAsync();

        Assert.Equal("Ctrl+Alt+Space", settings.Hotkey);
        Assert.Equal("Light", settings.Theme);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private SettingsStore CreateStore() => new(SettingsPath);
}

public sealed class StartupRegistrationTests : IDisposable
{
    private readonly string _keyPath = $"Software\\QuailTests\\{Guid.NewGuid():N}";

    [Fact]
    public void Absent_owned_value_is_disabled_enable_writes_quoted_command_and_disable_removes_it()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true)!;
        var registration = Create();

        Assert.False(registration.IsEnabled);
        Assert.Null(registration.SetEnabled(true));
        Assert.Equal($"\"{InstalledPath}\"", key.GetValue(StartupRegistration.ValueName));
        Assert.True(registration.IsEnabled);
        Assert.Null(registration.SetEnabled(false));
        Assert.Null(key.GetValue(StartupRegistration.ValueName));
        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void Unexpected_value_is_not_reported_as_enabled_and_is_replaced_only_when_enabled()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true)!;
        key.SetValue(StartupRegistration.ValueName, "C:\\unexpected.exe", RegistryValueKind.String);
        var registration = Create();

        Assert.False(registration.IsEnabled);
        Assert.Null(registration.SetEnabled(true));
        Assert.Equal($"\"{InstalledPath}\"", key.GetValue(StartupRegistration.ValueName));
    }

    [Fact]
    public void Development_path_cannot_create_a_persistent_startup_registration()
    {
        using var key = Registry.CurrentUser.CreateSubKey(_keyPath, writable: true)!;
        var registration = new StartupRegistration(() => Path.Combine(Path.GetTempPath(), "Quail.exe"), () => Registry.CurrentUser.OpenSubKey(_keyPath, writable: true));

        Assert.False(registration.IsEnabled);
        Assert.NotNull(registration.SetEnabled(true));
        Assert.Null(key.GetValue(StartupRegistration.ValueName));
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);

    private static string InstalledPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Quail", "Quail.exe");
    private StartupRegistration Create() => new(() => InstalledPath, () => Registry.CurrentUser.OpenSubKey(_keyPath, writable: true));
}
