using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class CopyQueueHostedService(
    IndexDatabase database,
    SettingsStore settingsStore,
    FileCopyService copyService,
    ILogger<CopyQueueHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await database.InitializeAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = await settingsStore.LoadAsync(stoppingToken);
                var queue = await database.GetAutomaticQueueAsync(100, stoppingToken);

                if (queue.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                for (var index = 0; index < queue.Count; index++)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var file = queue[index];
                    await ProcessOneAsync(file, settings, index + 1, queue.Count, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled copy queue error.");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }

    private async Task ProcessOneAsync(
        IndexedFile file,
        AppSettings settings,
        int currentFile,
        int totalFiles,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file.FullSourcePath))
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.SourceMissing,
                "Die Quelldatei ist nicht mehr vorhanden.", cancellationToken);
            return;
        }

        try
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.Copying, cancellationToken: cancellationToken);
            var progress = new Progress<CopyProgress>(p =>
            {
                if (p.Percent is 0 or 100 || p.Percent % 10 == 0)
                    logger.LogDebug("Copy {File}: {Percent}%", p.FileName, p.Percent);
            });

            var destination = await copyService.CopyAsync(file, settings, currentFile, totalFiles, progress, cancellationToken);
            await database.MarkCopiedAsync(file.Id, cancellationToken);
            logger.LogInformation("Copied {Source} to {Destination}.", file.FullSourcePath, destination);
        }
        catch (TargetUnavailableException ex)
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.WaitingForTarget, ex.Message, cancellationToken);
            logger.LogInformation("Target unavailable. {File} remains queued.", file.FileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.Indexed, cancellationToken: CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.Failed, ex.Message, cancellationToken);
            logger.LogError(ex, "Copy failed for {File}.", file.FullSourcePath);
        }
    }
}
