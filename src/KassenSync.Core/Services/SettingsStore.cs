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
        if (!File.Exists(AppPaths.SettingsPath))
        {
            var defaults = new AppSettings();
            await SaveAsync(defaults, cancellationToken);
            return defaults;
        }

        await using var stream = File.OpenRead(AppPaths.SettingsPath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
               ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        AppPaths.EnsureDirectories();
        var temp = AppPaths.SettingsPath + ".tmp";
        await using (var stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        File.Move(temp, AppPaths.SettingsPath, true);
    }
}
