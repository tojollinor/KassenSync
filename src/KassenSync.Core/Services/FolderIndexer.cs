using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class FolderIndexer(
    IndexDatabase database,
    FileHashService hashService,
    FileStabilityService stabilityService)
{
    public async Task<long?> TryIndexAsync(
        string fullPath,
        SyncJob job,
        SyncSide sourceSide,
        int stableDelayMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = GetSideRoot(job, sourceSide);
        if (!File.Exists(fullPath) ||
            !IsAllowed(fullPath, job.AllowedExtensions) ||
            !IsInsideRoot(fullPath, sourceRoot))
            return null;

        var stable = await stabilityService.WaitUntilStableAsync(
            fullPath,
            stableDelayMilliseconds,
            cancellationToken);

        if (!stable || !File.Exists(fullPath))
            return null;

        var full = Path.GetFullPath(fullPath);
        var info = new FileInfo(full);
        var sha256 = await hashService.ComputeSha256Async(full, cancellationToken);

        if (await database.ExistsAsync(job.Id, sourceSide, info.Name, sha256, cancellationToken))
            return null;

        var root = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, full);

        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        var item = new IndexedFile
        {
            JobId = job.Id,
            SourceSide = sourceSide,
            FileName = info.Name,
            RelativePath = relative,
            FullSourcePath = full,
            Sha256 = sha256,
            SizeBytes = info.Length,
            DetectedAtUtc = DateTime.UtcNow,
            Status = FileTransferStatus.Indexed
        };

        return await database.AddIfNewAsync(item, cancellationToken);
    }

    public async Task<int> ScanAsync(
        SyncJob job,
        SyncSide sourceSide,
        int stableDelayMilliseconds,
        CancellationToken cancellationToken = default)
    {
        var sourceRoot = GetSideRoot(job, sourceSide);
        if (!Directory.Exists(sourceRoot))
            return 0;

        var option = job.IncludeSubdirectories
            ? SearchOption.AllDirectories
            : SearchOption.TopDirectoryOnly;

        var added = 0;
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", option))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsTemporaryOrdnerSyncFile(file))
                continue;

            if (await TryIndexAsync(
                    file,
                    job,
                    sourceSide,
                    stableDelayMilliseconds,
                    cancellationToken) is not null)
                added++;
        }

        return added;
    }

    public async Task<long?> TryIndexAsync(
        string fullPath,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        SettingsMigration.Normalize(settings);
        var job = settings.Jobs.First();
        return await TryIndexAsync(
            fullPath,
            job,
            SyncSide.A,
            settings.FileStableDelayMilliseconds,
            cancellationToken);
    }

    public async Task<int> ScanAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        SettingsMigration.Normalize(settings);
        var job = settings.Jobs.First();
        return await ScanAsync(
            job,
            SyncSide.A,
            settings.FileStableDelayMilliseconds,
            cancellationToken);
    }

    private static string GetSideRoot(SyncJob job, SyncSide sourceSide)
        => sourceSide == SyncSide.A ? job.SourceFolder : job.TargetFolder;

    private static bool IsInsideRoot(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowed(
        string path,
        IReadOnlyCollection<string> allowedExtensions)
    {
        if (allowedExtensions.Count == 0)
            return true;

        var ext = Path.GetExtension(path);
        return allowedExtensions.Any(x =>
            string.Equals(
                NormalizeExtension(x),
                ext,
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTemporaryOrdnerSyncFile(string path)
        => Path.GetFileName(path).Contains(".ordnersync-", StringComparison.OrdinalIgnoreCase) &&
           path.EndsWith(".part", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExtension(string extension)
        => extension.StartsWith('.') ? extension : "." + extension;
}
