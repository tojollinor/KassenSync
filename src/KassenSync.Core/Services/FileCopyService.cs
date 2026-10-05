using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class FileCopyService(TargetPathService targetPathService)
{
    private const int BufferSize = 1024 * 1024;

    public async Task<string> CopyAsync(
        IndexedFile file,
        AppSettings settings,
        int currentFile,
        int totalFiles,
        IProgress<CopyProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(file.FullSourcePath))
            throw new FileNotFoundException("Die Quelldatei ist nicht mehr vorhanden.", file.FullSourcePath);

        if (!targetPathService.IsTargetAvailable(settings))
            throw new TargetUnavailableException(settings.TargetDrive);

        var destinationPath = targetPathService.GetDestinationPath(file, settings);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
                                   ?? throw new InvalidOperationException("Zielverzeichnis konnte nicht bestimmt werden.");
        Directory.CreateDirectory(destinationDirectory);

        var tempPath = destinationPath + $".kassensync-{Guid.NewGuid():N}.part";
        long copied = 0;

        try
        {
            await using (var source = new FileStream(
                             file.FullSourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough))
            {
                var totalBytes = source.Length;
                var buffer = new byte[BufferSize];
                while (true)
                {
                    var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                    if (read == 0)
                        break;

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    copied += read;
                    var percent = totalBytes <= 0 ? 100 : (int)Math.Clamp(copied * 100L / totalBytes, 0, 100);
                    progress?.Report(new CopyProgress(
                        file.Id,
                        file.FileName,
                        currentFile,
                        totalFiles,
                        copied,
                        totalBytes,
                        percent,
                        "copying"));
                }

                await target.FlushAsync(cancellationToken);
            }

            if (!targetPathService.IsTargetAvailable(settings))
                throw new TargetUnavailableException(settings.TargetDrive);

            File.Move(tempPath, destinationPath, true);
            File.SetLastWriteTimeUtc(destinationPath, File.GetLastWriteTimeUtc(file.FullSourcePath));

            progress?.Report(new CopyProgress(
                file.Id,
                file.FileName,
                currentFile,
                totalFiles,
                copied,
                Math.Max(file.SizeBytes, copied),
                100,
                "completed"));

            return destinationPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }
}

public sealed class TargetUnavailableException(string target)
    : IOException($"Das Zielmedium '{target}' ist nicht verfügbar.")
{
    public string Target { get; } = target;
}
