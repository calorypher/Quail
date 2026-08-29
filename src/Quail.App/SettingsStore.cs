using System.Text.Json;

namespace Quail.App;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quail",
            "settings.json"))
    {
    }

    internal SettingsStore(string path)
    {
        _path = path;
    }

    public async Task<ShellSettings> LoadAsync()
    {
        if (!File.Exists(_path))
        {
            AppLog.Write("Settings defaulted because no config exists.");
            return ShellSettings.Default;
        }

        try
        {
            await using var stream = File.OpenRead(_path);
            var settings = await JsonSerializer.DeserializeAsync<ShellSettings>(stream, SerializerOptions);
            if (settings is null || !HotkeyDefinition.TryParse(settings.Hotkey, out _))
            {
                throw new InvalidDataException("Settings file contains an invalid hotkey.");
            }

            return settings.Normalize();
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException)
        {
            AppLog.Write("Settings fallback to defaults after malformed configuration.", exception);
            return ShellSettings.Default;
        }
    }

    public async Task SaveAsync(ShellSettings settings)
    {
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, settings.Normalize(), SerializerOptions);
                await stream.FlushAsync();
            }

            File.Move(temporaryPath, _path, overwrite: true);
            AppLog.Write("Settings saved.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
