using KassenSync.Core.Models;

namespace KassenSync.Core.Services;

public sealed class FolderIndexer(
    IndexDatabase database,
    FileHashService hashService,
    FileStabilityService stabilityService)
{
    private enum BidirectionalDecision
    {
        Queue,
        Ignore,
        Conflict
    }

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
            !IsInsideRoot(fullPath, sourceRoot) ||
            IsTemporaryOrdnerSyncFile(fullPath))
            return null;

        var stable = await stabilityService.WaitUntilStableAsync(
            fullPath,
            stableDelayMilliseconds,
            cancellationToken);

        if (!stable || !File.Exists(fullPath))
            return null;

        var full = Path.GetFullPath(fullPath);
        var root = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var relative = Path.GetRelativePath(root, full);

        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        var info = new FileInfo(full);
        var sha256 = await hashService.ComputeSha256Async(full, cancellationToken);

        if (await database.ExistsAsync(
                job.Id,
                sourceSide,
                info.Name,
                sha256,
                cancellationToken))
            return null;

        var status = FileTransferStatus.Indexed;
        string? error = null;

        if (job.Mode == SyncMode.Bidirectional)
        {
            var decision = await EvaluateBidirectionalAsync(
                job,
                sourceSide,
                relative,
                sha256,
                stableDelayMilliseconds,
                cancellationToken);

            if (decision == BidirectionalDecision.Ignore)
                return null;

            if (decision == BidirectionalDecision.Conflict)
            {
                status = FileTransferStatus.Conflict;
                error =
                    "Konflikt: Die Datei wurde seit dem letzten gemeinsamen Stand auf beiden Seiten unterschiedlich geändert.";
            }
        }

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
            Status = status,
            LastError = error
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

    private async Task<BidirectionalDecision> EvaluateBidirectionalAsync(
        SyncJob job,
        SyncSide sourceSide,
        string relativePath,
        string sourceHash,
        int stableDelayMilliseconds,
        CancellationToken cancellationToken)
    {
        var baseline = await database.GetSyncBaselineAsync(
            job.Id,
            relativePath,
            cancellationToken);

        var oppositeSide = sourceSide == SyncSide.A
            ? SyncSide.B
            : SyncSide.A;

        var oppositeRoot = GetSideRoot(job, oppositeSide);
        var oppositePath = BuildPathInsideRoot(oppositeRoot, relativePath);

        if (!File.Exists(oppositePath))
        {
            // Wurde ein bereits synchronisiertes Gegenstück gelöscht,
            // wird es bewusst weder wiederhergestellt noch zurückgelöscht.
            return baseline is null
                ? BidirectionalDecision.Queue
                : BidirectionalDecision.Ignore;
        }

        var oppositeStable = await stabilityService.WaitUntilStableAsync(
            oppositePath,
            stableDelayMilliseconds,
            cancellationToken);

        if (!oppositeStable || !File.Exists(oppositePath))
            return BidirectionalDecision.Ignore;

        var oppositeHash = await hashService.ComputeSha256Async(
            oppositePath,
            cancellationToken);

        if (string.Equals(
                sourceHash,
                oppositeHash,
                StringComparison.OrdinalIgnoreCase))
        {
            await database.SetSyncBaselineAsync(
                job.Id,
                relativePath,
                sourceHash,
                cancellationToken);

            return BidirectionalDecision.Ignore;
        }

        if (baseline is null)
            return BidirectionalDecision.Conflict;

        var sourceChanged = !string.Equals(
            sourceHash,
            baseline,
            StringComparison.OrdinalIgnoreCase);

        var oppositeChanged = !string.Equals(
            oppositeHash,
            baseline,
            StringComparison.OrdinalIgnoreCase);

        if (sourceChanged && oppositeChanged)
            return BidirectionalDecision.Conflict;

        if (sourceChanged && !oppositeChanged)
            return BidirectionalDecision.Queue;

        if (!sourceChanged && oppositeChanged)
            return BidirectionalDecision.Ignore;

        return BidirectionalDecision.Conflict;
    }

    private static string GetSideRoot(SyncJob job, SyncSide sourceSide)
        => sourceSide == SyncSide.A
            ? job.SourceFolder
            : job.TargetFolder;

    private static string BuildPathInsideRoot(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fullPath, fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Der berechnete Gegenpfad liegt außerhalb des Sync-Ordners.");

        return fullPath;
    }

    private static bool IsInsideRoot(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(
            rootWithSeparator,
            StringComparison.OrdinalIgnoreCase);
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
        => Path.GetFileName(path).Contains(
               ".ordnersync-",
               StringComparison.OrdinalIgnoreCase) &&
           path.EndsWith(
               ".part",
               StringComparison.OrdinalIgnoreCase);

    private static string NormalizeExtension(string extension)
        => extension.StartsWith('.') ? extension : "." + extension;
}
