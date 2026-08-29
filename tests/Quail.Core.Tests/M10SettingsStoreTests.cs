using Quail.App;

namespace Quail.Core.Tests;

public sealed class M10SettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"quail-m10-settings-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadAsync_UsesDefaultsWhenConfigDoesNotExist()
    {
        var settings = await CreateStore().LoadAsync();

        Assert.Equal(ShellSettings.Default, settings);
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
