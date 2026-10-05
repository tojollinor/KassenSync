using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class FolderWatchHostedService(
    SettingsStore settingsStore,
    FolderIndexer indexer,
    ILogger<FolderWatchHostedService> logger) : BackgroundService
{
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _indexGate = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = await settingsStore.LoadAsync(stoppingToken);
            ConfigureWatcher(settings, stoppingToken);

            try
            {
                var count = await indexer.ScanAsync(settings, stoppingToken);
                if (count > 0)
                    logger.LogInformation("Safety scan indexed {Count} new file(s).", count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Safety scan failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, settings.RescanIntervalSeconds)), stoppingToken);
        }
    }

    private void ConfigureWatcher(AppSettings settings, CancellationToken stoppingToken)
    {
        if (!Directory.Exists(settings.SourceFolder))
        {
            _watcher?.Dispose();
            _watcher = null;
            return;
        }

        if (_watcher is not null &&
            string.Equals(_watcher.Path, Path.GetFullPath(settings.SourceFolder), StringComparison.OrdinalIgnoreCase) &&
            _watcher.IncludeSubdirectories == settings.IncludeSubdirectories)
            return;

        _watcher?.Dispose();
        _watcher = new FileSystemWatcher(Path.GetFullPath(settings.SourceFolder))
        {
            IncludeSubdirectories = settings.IncludeSubdirectories,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite | NotifyFilters.Size,
            Filter = "*",
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };

        _watcher.Created += (_, e) => QueueIndex(e.FullPath, stoppingToken);
        _watcher.Renamed += (_, e) => QueueIndex(e.FullPath, stoppingToken);
        _watcher.Changed += (_, e) => QueueIndex(e.FullPath, stoppingToken);
        _watcher.Error += (_, e) => logger.LogWarning(e.GetException(), "FileSystemWatcher error; periodic rescan remains active.");
        logger.LogInformation("Watching {Source} (subdirectories: {Include}).", settings.SourceFolder, settings.IncludeSubdirectories);
    }

    private void QueueIndex(string path, CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            await _indexGate.WaitAsync(stoppingToken);
            try
            {
                var settings = await settingsStore.LoadAsync(stoppingToken);
                var id = await indexer.TryIndexAsync(path, settings, stoppingToken);
                if (id is not null)
                    logger.LogInformation("Indexed {Path} as #{Id}.", path, id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not index {Path}.", path);
            }
            finally
            {
                _indexGate.Release();
            }
        }, stoppingToken);
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _indexGate.Dispose();
        base.Dispose();
    }
}
