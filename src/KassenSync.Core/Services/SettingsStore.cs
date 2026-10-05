using System.Text.Json;
using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();

        AppSettings settings;
        if (!File.Exists(AppPaths.SettingsPath))
        {
            settings = new AppSettings();
            SettingsMigration.Normalize(settings);
            await SaveAsync(settings, cancellationToken);
            return settings;
        }

        await using (var stream = File.OpenRead(AppPaths.SettingsPath))
        {
            settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                       ?? new AppSettings();
        }

        if (SettingsMigration.Normalize(settings))
            await SaveAsync(settings, cancellationToken);

        return settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        SettingsMigration.Normalize(settings);

        var temp = AppPaths.SettingsPath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);

        File.Move(temp, AppPaths.SettingsPath, true);
    }
}
