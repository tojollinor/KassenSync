using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class FolderWatchHostedService(
    SettingsStore settingsStore,
    FolderIndexer indexer,
    ILogger<FolderWatchHostedService> logger) : BackgroundService
{
    private readonly Dictionary<string, WatchRegistration> _watchers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private DateTime _nextSafetyScanUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await settingsStore.LoadAsync(stoppingToken);
                ConfigureWatchers(settings, stoppingToken);

                if (DateTime.UtcNow >= _nextSafetyScanUtc)
                {
                    await RunSafetyScanAsync(settings, stoppingToken);
                    _nextSafetyScanUtc = DateTime.UtcNow.AddSeconds(
                        Math.Max(5, settings.RescanIntervalSeconds));
                }

                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Folder watcher loop failed.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private void ConfigureWatchers(
        AppSettings settings,
        CancellationToken stoppingToken)
    {
        var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var job in settings.Jobs.Where(x => x.Enabled))
        {
            ConfigureWatcher(
                job,
                SyncSide.A,
                settings.FileStableDelayMilliseconds,
                expectedKeys,
                stoppingToken);

            if (job.Mode == SyncMode.Bidirectional)
            {
                ConfigureWatcher(
                    job,
                    SyncSide.B,
                    settings.FileStableDelayMilliseconds,
                    expectedKeys,
                    stoppingToken);
            }
        }

        foreach (var key in _watchers.Keys
                     .Where(x => !expectedKeys.Contains(x))
                     .ToArray())
        {
            _watchers[key].Dispose();
            _watchers.Remove(key);
        }
    }

    private void ConfigureWatcher(
        SyncJob job,
        SyncSide side,
        int stableDelayMilliseconds,
        ISet<string> expectedKeys,
        CancellationToken stoppingToken)
    {
        var root = side == SyncSide.A
            ? job.SourceFolder
            : job.TargetFolder;

        var key = BuildKey(job.Id, side);

        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            if (_watchers.Remove(key, out var old))
                old.Dispose();
            return;
        }

        var fullRoot = Path.GetFullPath(root);
        expectedKeys.Add(key);

        if (_watchers.TryGetValue(key, out var existing) &&
            string.Equals(existing.Root, fullRoot, StringComparison.OrdinalIgnoreCase) &&
            existing.IncludeSubdirectories == job.IncludeSubdirectories)
            return;

        if (_watchers.Remove(key, out var replaced))
            replaced.Dispose();

        var watcher = new FileSystemWatcher(fullRoot)
        {
            IncludeSubdirectories = job.IncludeSubdirectories,
            NotifyFilter =
                NotifyFilters.FileName |
                NotifyFilters.CreationTime |
                NotifyFilters.LastWrite |
                NotifyFilters.Size,
            Filter = "*",
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };

        watcher.Created += (_, e) =>
            QueueIndex(job.Id, side, e.FullPath, stableDelayMilliseconds, stoppingToken);
        watcher.Renamed += (_, e) =>
            QueueIndex(job.Id, side, e.FullPath, stableDelayMilliseconds, stoppingToken);
        watcher.Changed += (_, e) =>
            QueueIndex(job.Id, side, e.FullPath, stableDelayMilliseconds, stoppingToken);
        watcher.Error += (_, e) =>
            logger.LogWarning(
                e.GetException(),
                "FileSystemWatcher error for job {Job}; safety scan remains active.",
                job.Name);

        _watchers[key] = new WatchRegistration(
            watcher,
            fullRoot,
            job.IncludeSubdirectories);

        logger.LogInformation(
            "Watching job {Job} side {Side}: {Source} (subdirectories: {Include}).",
            job.Name,
            side,
            fullRoot,
            job.IncludeSubdirectories);
    }

    private async Task RunSafetyScanAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        foreach (var job in settings.Jobs.Where(x => x.Enabled))
        {
            try
            {
                var countA = await indexer.ScanAsync(
                    job,
                    SyncSide.A,
                    settings.FileStableDelayMilliseconds,
                    cancellationToken);

                var countB = 0;
                if (job.Mode == SyncMode.Bidirectional)
                {
                    countB = await indexer.ScanAsync(
                        job,
                        SyncSide.B,
                        settings.FileStableDelayMilliseconds,
                        cancellationToken);
                }

                var total = countA + countB;
                if (total > 0)
                    logger.LogInformation(
                        "Safety scan for job {Job} indexed {Count} new file(s).",
                        job.Name,
                        total);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Safety scan failed for job {Job}.",
                    job.Name);
            }
        }
    }

    private void QueueIndex(
        string jobId,
        SyncSide side,
        string path,
        int stableDelayMilliseconds,
        CancellationToken stoppingToken)
    {
        _ = Task.Run(async () =>
        {
            if (stoppingToken.IsCancellationRequested)
                return;

            await _indexGate.WaitAsync(stoppingToken);
            try
            {
                var settings = await settingsStore.LoadAsync(stoppingToken);
                var job = settings.Jobs.FirstOrDefault(x =>
                    x.Enabled &&
                    string.Equals(x.Id, jobId, StringComparison.OrdinalIgnoreCase));

                if (job is null)
                    return;

                if (side == SyncSide.B && job.Mode != SyncMode.Bidirectional)
                    return;

                var id = await indexer.TryIndexAsync(
                    path,
                    job,
                    side,
                    stableDelayMilliseconds,
                    stoppingToken);

                if (id is not null)
                    logger.LogInformation(
                        "Indexed {Path} for job {Job} as #{Id}.",
                        path,
                        job.Name,
                        id);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
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

    private static string BuildKey(string jobId, SyncSide side)
        => $"{jobId}:{side}";

    public override void Dispose()
    {
        foreach (var watcher in _watchers.Values)
            watcher.Dispose();

        _watchers.Clear();
        _indexGate.Dispose();
        base.Dispose();
    }

    private sealed class WatchRegistration(
        FileSystemWatcher watcher,
        string root,
        bool includeSubdirectories) : IDisposable
    {
        public string Root { get; } = root;
        public bool IncludeSubdirectories { get; } = includeSubdirectories;

        public void Dispose() => watcher.Dispose();
    }
}
