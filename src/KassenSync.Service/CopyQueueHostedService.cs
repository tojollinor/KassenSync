using KassenSync.Core.Models;
using KassenSync.Core.Services;

namespace KassenSync.Service;

public sealed class CopyQueueHostedService(
    IndexDatabase database,
    SettingsStore settingsStore,
    FileCopyService copyService,
    TargetPathService targetPathService,
    CopyStateStore copyStateStore,
    ILogger<CopyQueueHostedService> logger) : BackgroundService
{
    private enum CopyResult { Copied, WaitingForTarget, Failed, SourceMissing }

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

                var operationId = copyStateStore.Begin(queue.Count);
                var copied = 0;
                string? failure = null;

                for (var index = 0; index < queue.Count; index++)
                {
                    stoppingToken.ThrowIfCancellationRequested();
                    var result = await ProcessOneAsync(queue[index], settings, index + 1, queue.Count, operationId, stoppingToken);
                    switch (result)
                    {
                        case CopyResult.Copied:
                            copied++;
                            break;
                        case CopyResult.WaitingForTarget when copied > 0:
                            failure ??= "Das Zielmedium ist nicht mehr verfügbar. Nicht alle Dateien konnten kopiert werden.";
                            break;
                        case CopyResult.Failed:
                            failure ??= "Mindestens eine Datei konnte nicht kopiert werden.";
                            break;
                        case CopyResult.SourceMissing:
                            failure ??= "Mindestens eine Quelldatei ist nicht mehr vorhanden.";
                            break;
                    }
                }

                if (failure is not null)
                    copyStateStore.CompleteFailure(operationId, failure);
                else if (copied > 0)
                    copyStateStore.CompleteSuccess(operationId, copied);

                await Task.Delay(TimeSpan.FromMilliseconds(400), stoppingToken);
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

    private async Task<CopyResult> ProcessOneAsync(
        IndexedFile file,
        AppSettings settings,
        int currentFile,
        int totalFiles,
        string operationId,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(file.FullSourcePath))
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.SourceMissing,
                "Die Quelldatei ist nicht mehr vorhanden.", cancellationToken);
            return CopyResult.SourceMissing;
        }

        if (!targetPathService.IsTargetAvailable(settings))
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.WaitingForTarget,
                $"Das Zielmedium '{settings.TargetDrive}' ist nicht verfügbar.", cancellationToken);
            return CopyResult.WaitingForTarget;
        }

        try
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.Copying, cancellationToken: cancellationToken);
            copyStateStore.StartFile(operationId, currentFile, totalFiles, file.FileName);
            var progress = new InlineProgress<CopyProgress>(p => copyStateStore.Report(operationId, p));
            var destination = await copyService.CopyAsync(file, settings, currentFile, totalFiles, progress, cancellationToken);
            await database.MarkCopiedAsync(file.Id, cancellationToken);
            logger.LogInformation("Copied {Source} to {Destination}.", file.FullSourcePath, destination);
            return CopyResult.Copied;
        }
        catch (TargetUnavailableException ex)
        {
            await database.SetStatusAsync(file.Id, FileTransferStatus.WaitingForTarget, ex.Message, cancellationToken);
            logger.LogInformation("Target unavailable. {File} remains queued.", file.FileName);
            return CopyResult.WaitingForTarget;
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
            return CopyResult.Failed;
        }
    }

    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }
}
