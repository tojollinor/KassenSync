using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class Worker(IndexDatabase database, SettingsStore settingsStore, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await database.InitializeAsync(stoppingToken);
        var settings = await settingsStore.LoadAsync(stoppingToken);
        logger.LogInformation("KassenSync service started. Source: {Source}, Target: {Target}",
            settings.SourceFolder, Path.Combine(settings.TargetDrive, settings.TargetSubfolder));

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
    }
}
